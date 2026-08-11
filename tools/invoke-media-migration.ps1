param(
    [ValidateSet('DryRun', 'Canary', 'Apply', 'Rollback')]
    [string]$Mode = 'DryRun',
    [string]$MigrationProject = (Join-Path $PSScriptRoot 'PTfinder.MediaMigration\PTfinder.MediaMigration.csproj'),
    [string]$BackendProject = (Join-Path $PSScriptRoot '..\PTfinder.API\PTfinder.API.csproj'),
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\migration-backups\cloudflare-media-manifest.json'),
    [switch]$BeginAuthorization
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$clientId = '04b07795-8ddb-461a-bbee-02f9e1bf7b46'
$tenant = 'organizations'
$scope = 'https://management.azure.com/.default offline_access openid profile'
$ruleName = 'PTfinderMediaMigration'
$authorizationStatePath = Join-Path $env:TEMP 'ptfindernow-media-migration-azure-auth.json'

function Start-AzureManagementAuthorization {
    $device = Invoke-RestMethod `
        -Method Post `
        -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/devicecode" `
        -ContentType 'application/x-www-form-urlencoded' `
        -Body @{ client_id = $clientId; scope = $scope }

    @{
        client_id = $clientId
        device_code = $device.device_code
        interval = [int]$device.interval
        expires_at_utc = [DateTimeOffset]::UtcNow.AddSeconds([int]$device.expires_in).ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath $authorizationStatePath -Encoding UTF8

    Write-Host ''
    Write-Host 'Azure sign-in is required for a temporary one-IP SQL firewall rule.' -ForegroundColor Cyan
    Write-Host "Open: $($device.verification_uri)" -ForegroundColor Yellow
    Write-Host "Code: $($device.user_code)" -ForegroundColor Yellow
    Start-Process -FilePath $device.verification_uri
}

function Get-AzureManagementToken {
    if (-not (Test-Path -LiteralPath $authorizationStatePath)) {
        throw 'Azure authorization was not started. Run with -BeginAuthorization first.'
    }

    $state = Get-Content -LiteralPath $authorizationStatePath -Raw | ConvertFrom-Json
    $deadline = [DateTimeOffset]::Parse($state.expires_at_utc)
    $interval = [Math]::Max(5, [int]$state.interval)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            return Invoke-RestMethod `
                -Method Post `
                -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" `
                -ContentType 'application/x-www-form-urlencoded' `
                -Body @{
                    grant_type = 'urn:ietf:params:oauth:grant-type:device_code'
                    client_id = $state.client_id
                    device_code = $state.device_code
                }
        }
        catch {
            $details = $null
            if ($_.ErrorDetails.Message) {
                try { $details = $_.ErrorDetails.Message | ConvertFrom-Json } catch { }
            }
            if ($details.error -eq 'authorization_pending') {
                Start-Sleep -Seconds $interval
                continue
            }
            if ($details.error -eq 'slow_down') {
                $interval += 5
                Start-Sleep -Seconds $interval
                continue
            }
            throw
        }
    }

    throw 'Azure device authorization expired.'
}

function Get-BackendSecrets {
    $raw = dotnet user-secrets list --project $BackendProject --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read backend user-secrets.' }
    $json = (($raw | Where-Object { $_ -notmatch '^//(BEGIN|END)' }) -join [Environment]::NewLine)
    return $json | ConvertFrom-Json
}

function Find-SqlServerResource($headers, [string]$serverName) {
    $subscriptions = Invoke-RestMethod `
        -Uri 'https://management.azure.com/subscriptions?api-version=2020-01-01' `
        -Headers $headers

    $matches = @()
    foreach ($subscription in $subscriptions.value) {
        try {
            $servers = Invoke-RestMethod `
                -Uri "https://management.azure.com/subscriptions/$($subscription.subscriptionId)/providers/Microsoft.Sql/servers?api-version=2023-08-01" `
                -Headers $headers
            foreach ($server in $servers.value) {
                if ($server.name -eq $serverName) {
                    $matches += [PSCustomObject]@{
                        SubscriptionId = $subscription.subscriptionId
                        SubscriptionName = $subscription.displayName
                        Server = $server
                    }
                }
            }
        }
        catch {
            Write-Warning "Could not inspect SQL servers in subscription '$($subscription.displayName)'."
        }
    }

    if ($matches.Count -ne 1) {
        throw "Expected one Azure SQL server named '$serverName'; found $($matches.Count)."
    }
    return $matches[0]
}

function Invoke-Migration([string[]]$Arguments) {
    & dotnet run `
        --project $MigrationProject `
        --configuration Release `
        --no-build `
        -- `
        @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The media migration tool exited with code $LASTEXITCODE."
    }
}

if ($BeginAuthorization) {
    Start-AzureManagementAuthorization
    return
}

$secrets = Get-BackendSecrets
$connectionString = [string]$secrets.PSObject.Properties['ConnectionStrings:mycon'].Value
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw 'The local database connection string is missing.'
}

$connection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connectionString
$sqlServerName = ([string]$connection.DataSource).Split('.')[0]
$connectionString = $null
$connection = $null
$secrets = $null

$token = Get-AzureManagementToken
$headers = @{ Authorization = "Bearer $($token.access_token)"; Accept = 'application/json' }
$resource = Find-SqlServerResource $headers $sqlServerName
if ($resource.Server.id -notmatch '/resourceGroups/([^/]+)/') {
    throw 'Could not determine the SQL server resource group.'
}

$resourceGroup = $Matches[1]
$subscriptionId = $resource.SubscriptionId
$publicIp = [string](Invoke-RestMethod -Uri 'https://api.ipify.org')
if ($publicIp -notmatch '^\d{1,3}(\.\d{1,3}){3}$') {
    throw 'Could not determine the current public IPv4 address.'
}

$firewallUri = "https://management.azure.com/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Sql/servers/$sqlServerName/firewallRules/$ruleName?api-version=2023-08-01"
$firewallBody = @{
    properties = @{
        startIpAddress = $publicIp
        endIpAddress = $publicIp
    }
} | ConvertTo-Json -Depth 4 -Compress

$firewallCreated = $false
try {
    $null = Invoke-RestMethod `
        -Method Put `
        -Uri $firewallUri `
        -Headers $headers `
        -ContentType 'application/json' `
        -Body $firewallBody
    $firewallCreated = $true
    Write-Host "Temporary SQL access opened only for $publicIp." -ForegroundColor Green
    Start-Sleep -Seconds 8

    switch ($Mode) {
        'DryRun' {
            Invoke-Migration @('--manifest', $ManifestPath)
        }
        'Canary' {
            Invoke-Migration @('--apply', '--kind', 'profile', '--max', '5', '--manifest', $ManifestPath)
            Invoke-Migration @('--apply', '--kind', 'gallery', '--media', 'image', '--max', '10', '--manifest', $ManifestPath)
            Invoke-Migration @('--apply', '--kind', 'gallery', '--media', 'video', '--max', '2', '--manifest', $ManifestPath)
        }
        'Apply' {
            Invoke-Migration @('--apply', '--manifest', $ManifestPath)
        }
        'Rollback' {
            Invoke-Migration @('--rollback', '--manifest', $ManifestPath)
        }
    }
}
finally {
    if ($firewallCreated) {
        try {
            Invoke-RestMethod -Method Delete -Uri $firewallUri -Headers $headers | Out-Null
            Write-Host 'Temporary SQL firewall rule removed.' -ForegroundColor Green
        }
        catch {
            Write-Error "IMPORTANT: Could not remove temporary SQL firewall rule '$ruleName'. Remove it in Azure immediately."
        }
    }
    $token = $null
    $headers = $null
    Remove-Item -LiteralPath $authorizationStatePath -Force -ErrorAction SilentlyContinue
}
