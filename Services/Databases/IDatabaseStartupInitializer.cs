using System.Threading.Tasks;

namespace FaceLocker.Services
{
    public interface IDatabaseStartupInitializer
    {
        Task InitializeAsync();
    }
}