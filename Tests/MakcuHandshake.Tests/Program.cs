using MystieHook.Input;

int checks = 0;
void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    checks++;
}
string Read(string reply, bool fragmented = false)
{
    int index = 0, calls = 0;
    return MakcuHandshake.ReadVersion(() =>
    {
        if (fragmented && ++calls % 3 == 0) throw new TimeoutException();
        return index < reply.Length ? reply[index++] : -1;
    }, 500);
}
Check(Read("km.version()\r\n>>> km.MAKCU v1.2\r\n") == ">>> km.MAKCU v1.2", "Ignore command echo and retain version");
Check(Read("\0\x01\r\nkm.MAKCU v2\n", true) == "km.MAKCU v2", "Fragmented response survives intervening timeouts/button bytes");
Check(Read("km.version()\r\n") == "", "Echo alone cannot authenticate");
Check(Read("different device\n") == "", "Unrelated serial device rejected");
Check(Read("km.makcu v3") == "km.makcu v3", "Partial final line and case-insensitive signature");
Check(Read("km.MAKCU\r\nv3.7\r\n>>> ",true)=="km.MAKCU\nv3.7","Retain version details after device signature");
Check(Read("km.MAKCU\r\n>>> ")=="km.MAKCU","Signature-only reply does not invent a firmware version");
Check(Read(new string('x', 5000) + "km.MAKCU") == "", "Unbounded input rejected");
Check(MakcuHandshake.ReadVersion(() => throw new TimeoutException(), 10) == "", "No reply times out");
bool propagated = false;
try { MakcuHandshake.ReadVersion(() => throw new IOException("unplugged"), 500); }
catch (IOException) { propagated = true; }
Check(propagated, "Device failure reaches connection cleanup");
Console.WriteLine($"All {checks} Makcu handshake checks passed. No hardware accessed.");
