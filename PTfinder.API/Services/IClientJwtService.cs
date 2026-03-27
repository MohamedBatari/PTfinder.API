using PTfinder.API.Models;

namespace PTfinder.API.Services
{
    public interface IClientJwtService
    {
        string GenerateToken(Client client);
    }
}