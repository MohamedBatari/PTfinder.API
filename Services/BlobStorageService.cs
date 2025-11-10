using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace PTfinder.API.Services
{
    public class BlobStorageService
    {
        private readonly BlobContainerClient _container;

        public BlobStorageService(IConfiguration config)
        {
            var conn = config["AzureStorage:ConnectionString"];
            var containerName = config["AzureStorage:Container"] ?? "media";

            if (!string.IsNullOrWhiteSpace(conn))
            {
                var service = new BlobServiceClient(conn);
                _container = service.GetBlobContainerClient(containerName);
                _container.CreateIfNotExists();
                return;
            }

            var accountUrl = config["AzureStorage:AccountUrl"]; 
            if (!string.IsNullOrWhiteSpace(accountUrl))
            {
                var service = new BlobServiceClient(new Uri(accountUrl), new Azure.Identity.DefaultAzureCredential());
                _container = service.GetBlobContainerClient(containerName);
                _container.CreateIfNotExists();
                return;
            }

            throw new Exception("Blob storage is not configured.");
        }

        public async Task<string> UploadAsync(string fileName, Stream stream, string contentType)
        {
            var blob = _container.GetBlobClient(fileName);
            await blob.UploadAsync(stream, overwrite: true);
            await blob.SetHttpHeadersAsync(new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType });
            return blob.Name; 
        }

        public string GetReadUrl(string blobName, TimeSpan ttl)
        {
            var blob = _container.GetBlobClient(blobName);

            if (blob.CanGenerateSasUri)
            {
                var sas = new BlobSasBuilder
                {
                    BlobContainerName = _container.Name,
                    BlobName = blobName,
                    Resource = "b",
                    ExpiresOn = DateTimeOffset.UtcNow.Add(ttl)
                };
                sas.SetPermissions(BlobSasPermissions.Read);
                return blob.GenerateSasUri(sas).ToString();
            }

            return blob.Uri.ToString();
        }

        public async Task DeleteAsync(string blobName)
        {
            await _container.DeleteBlobIfExistsAsync(blobName);
        }

    }
}

