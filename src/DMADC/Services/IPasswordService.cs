using System.Threading.Tasks;

namespace DMADC.Services;

public interface IPasswordService
{
    Task<bool> VerifyAsync(string inputPassword);
    Task ChangePasswordAsync(string oldPassword, string newPassword);
    Task<bool> IsFirstTimeSetupAsync();
}
