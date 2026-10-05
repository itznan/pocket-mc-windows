using System.Text.Json.Serialization;

namespace PocketMC.Domain.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CloudBackupProviderType
{
    GoogleDrive,
    Dropbox,
    OneDrive
}
