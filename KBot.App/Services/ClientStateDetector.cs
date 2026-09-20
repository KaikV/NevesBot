using KBot.App.Models;

namespace KBot.App.Services
{
    public sealed record ClientStateEvidence(CharacterPresence State, bool Reliable, double Confidence);

    // Pure client-side (memory) reader. No WPF dependency so it can be unit
    // tested headless. The core fills CharacterState when it can classify the
    // character; otherwise the valid world position read is used as proof that
    // a character is loaded and in-world.
    public sealed class ClientStateDetector
    {
        public ClientStateEvidence Detect(NativeStatus? status, int expectedPid)
        {
            if (status?.Pid != expectedPid || status.ReaderStatus != "READY")
                return new(CharacterPresence.Unknown, false, 0);

            var explicitState = status.CharacterState?.ToUpperInvariant();
            if (explicitState is not null)
                return explicitState switch
                {
                    "INGAME" => new(CharacterPresence.InGame, true, 0.99),
                    "LOGINSCREEN" => new(CharacterPresence.LoginScreen, true, 0.99),
                    "CHARACTERSELECTION" => new(CharacterPresence.CharacterSelection, true, 0.99),
                    "LOADING" => new(CharacterPresence.Loading, true, 0.99),
                    "DISCONNECTED" => new(CharacterPresence.Disconnected, true, 0.99),
                    _ => new(CharacterPresence.Unknown, false, 0)
                };

            if (status.HasPosition && (status.PosX != 0 || status.PosY != 0 || status.PosZ != 0))
                return new(CharacterPresence.InGame, true, 0.95);

            return new(CharacterPresence.Unknown, false, 0);
        }
    }
}
