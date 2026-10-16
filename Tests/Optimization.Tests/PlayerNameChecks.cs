using System.Text;
using MystieHook.Games.Wardog;
using O = MystieHook.Games.Wardog.WardogOffsets;

internal static class PlayerNameChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Literal offsets and separate buffers prevent the fixture following a
        // mistaken production offset, pawn-relative read or capacity-sized read.
        static void Name(Fixture f, ulong header, ulong data, string name, int extraCapacity = 0)
        {
            f.Put(header, data);
            f.Put(header + 8, name.Length + 1);
            f.Put(header + 12, name.Length + 1 + extraCapacity);
            byte[] bytes = Encoding.Unicode.GetBytes(name + "\0");
            for (int i = 0; i < bytes.Length; i++) f.Put(data + (ulong)i, bytes[i]);
        }
        const ulong secondState = 0xD00000, secondPawn = 0xD10000, secondRoot = 0xD20000;
        const ulong firstText = 0xD30000, secondText = 0xD40000;
        const string expected = "Élodie 玩家 🚀";
        var f = new Fixture();
        f.Array(Fixture.GameState + O.GameStatePlayerArray, Fixture.States, 2);
        f.Put(Fixture.States + 8, secondState);
        f.Put(secondState + O.PlayerStatePawn, secondPawn);
        f.Put(secondPawn + O.UObjectClass, Fixture.PlayerClass);
        f.Put(secondPawn + O.ActorRootComponent, secondRoot);
        f.Put(secondRoot + O.SceneRelativeLocation, new WardogReader.Vec { X = 1200 });
        Name(f, Fixture.State + 0x430, firstText, expected, 32);
        Name(f, secondState + 0x430, secondText, "Друг", 16);
        Name(f, Fixture.State + 0x350, 0xD50000, "WrongOldOffset");
        Name(f, Fixture.State + 0x43, 0xD60000, "WrongShortOffset");
        Name(f, Fixture.Actor + 0x430, 0xD70000, "WrongPawnOffset");
        Name(f, secondPawn + 0x430, 0xD80000, "WrongOtherPawn");
        var reader = f.Reader();
        var frame = reader.Capture(radar:new(Enabled:true, Names:true, Range:2000));
        check(frame.Valid && frame.Players.Single(p=>p.SourceState==Fixture.State).Name==expected
            && frame.Players.Single(p=>p.SourceState==secondState).Name=="Друг",
            "player labels read each PlayerState +430 FString header (8/4/4) and UTF-16 buffer");
        check(frame.RadarPlayers.Single(p=>p.Address==Fixture.Actor).Name==expected
            && frame.RadarPlayers.Single(p=>p.Address==secondPawn).Name=="Друг",
            "radar and overlay share correctly decoded names from distinct PlayerStates");
        check(f.Reads.ContainsKey(Fixture.State+0x430) && f.Reads.ContainsKey(secondState+0x430)
            && !f.Reads.ContainsKey(Fixture.State+0x350) && !f.Reads.ContainsKey(Fixture.State+0x43)
            && !f.Reads.ContainsKey(Fixture.Actor+0x430) && !f.Reads.ContainsKey(secondPawn+0x430),
            "name acquisition ignores old offsets and pawn-relative decoys");
        check(f.Reads.ContainsKey(firstText) && f.Reads.ContainsKey(secondText),
            "UTF-16 decoding reads count-sized pointed buffers rather than spare capacity");

        var retry = new Fixture();
        Name(retry, Fixture.State+0x430, firstText, expected);
        retry.Put(Fixture.State+0x43C, 1); // Capacity smaller than character count.
        var retryReader = retry.Reader();
        check(retryReader.Capture().Players.Single().Name=="Player", "invalid FString capacity is rejected safely");
        Name(retry, Fixture.State+0x430, firstText, expected); retry.Now=1000;
        check(retryReader.Capture().Players.Single().Name==expected,
            "unavailable player name retries and recovers at the existing metadata deadline");
        var unreadable = new Fixture();
        Name(unreadable, Fixture.State+0x430, firstText, expected);
        unreadable.Memory.Remove(firstText+2);
        check(unreadable.Reader().Capture().Players.Single().Name=="Player",
            "partial UTF-16 reads cannot expose a malformed or partial player label");
    }
}
