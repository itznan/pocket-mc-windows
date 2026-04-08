using System.IO;
using System.Threading.Tasks;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class PhysicalFileSystem : IFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public bool FileExists(string path) => File.Exists(path);

        public Task WriteAllTextAsync(string path, string contents) => File.WriteAllTextAsync(path, contents);

        public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);

        public Task WriteAllBytesAsync(string path, byte[] bytes) => File.WriteAllBytesAsync(path, bytes);

        public Task DeleteFileAsync(string path) => Task.Run(() => File.Delete(path));

        public string CombinePath(params string[] paths) => Path.Combine(paths);
    }
}
