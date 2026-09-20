using System.Text.Json.Serialization;

namespace KBot.App.Models
{
    public class NativeStatus
    {
        [JsonPropertyName("nativeOnline")]
        public bool NativeOnline { get; set; }

        [JsonPropertyName("clientFound")]
        public bool ClientFound { get; set; }

        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        [JsonPropertyName("processName")]
        public string? ProcessName { get; set; }

        [JsonPropertyName("hwnd")]
        public long Hwnd { get; set; }

        [JsonPropertyName("readerStatus")]
        public string? ReaderStatus { get; set; }

        [JsonPropertyName("readerMessage")]
        public string? ReaderMessage { get; set; }

        [JsonPropertyName("characterState")]
        public string? CharacterState { get; set; }
    }
}
