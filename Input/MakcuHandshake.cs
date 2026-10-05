using System;
using System.Diagnostics;
using System.Text;

namespace MystieHook.Input;

internal static class MakcuHandshake
{
    internal static string ReadVersion(Func<int> readByte, int timeoutMs)
    {
        var line = new StringBuilder();
        var reply = new StringBuilder();
        bool signature = false;
        void CompleteLine()
        {
            string text=line.ToString().Trim();line.Clear();
            if(IsVersion(text))signature=true;
            if(signature&&text.Length>0&&text!=" >>>"&&text!=">>>")
            {if(reply.Length>0)reply.Append('\n');reply.Append(text);}
        }
        var elapsed = Stopwatch.StartNew();
        int received = 0;
        while (elapsed.ElapsedMilliseconds < timeoutMs && received < 4096)
        {
            int next;
            try { next = readByte(); }
            catch (TimeoutException) { continue; }
            if (next < 0) break;
            received++;
            if (next == '\r' || next == '\n')
            {
                CompleteLine();
            }
            else if (next >= 32 && next <= 126) line.Append((char)next);
        }
        CompleteLine();
        return signature ? reply.ToString() : "";
    }

    private static bool IsVersion(string line) =>
        line.Contains("km.MAKCU", StringComparison.OrdinalIgnoreCase);
}
