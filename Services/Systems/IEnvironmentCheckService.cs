using System.Threading.Tasks;

namespace FaceLocker.Services
{
    public interface IEnvironmentCheckService
    {
        Task<bool> CheckDatabaseAsync();

        Task<bool> CheckCameraAsync();

        Task<bool> CheckLockControlBoardAsync();

        Task<bool> CheckBaiduFaceSDKAsync();

        Task<SystemCheckResult> RunFullCheckAsync();
    }
}
