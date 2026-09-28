using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace SNTerm.Services;

public static class ClipboardService
{
    public static bool SetText(string text, int retries = 5, int delayMs = 50)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(delayMs);
            }
            catch (ExternalException)
            {
                Thread.Sleep(delayMs);
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public static string? GetText(int retries = 5, int delayMs = 50)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    return Clipboard.GetText();
                }
                return null;
            }
            catch (COMException)
            {
                Thread.Sleep(delayMs);
            }
            catch (ExternalException)
            {
                Thread.Sleep(delayMs);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}
