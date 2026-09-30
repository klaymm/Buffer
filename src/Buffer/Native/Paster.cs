using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Buffer
{
    static class Paster
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;
        static readonly ushort[] StuckModifiers = { 0x5B, 0x5C, 0xA0, 0xA1, 0xA4, 0xA5 };

        public static async Task PasteInto(IntPtr target)
        {
            try
            {
                if (target == IntPtr.Zero || !NativeMethods.IsWindow(target))
                    return;

                NativeMethods.SetForegroundWindow(target);
                for (int i = 0; i < 25 && NativeMethods.GetForegroundWindow() != target; i++)
                    await Task.Delay(10);

                // Окну нужно мгновение, чтобы после активации вернуть фокус своему полю ввода.
                await Task.Delay(40);

                // Зажатые Win, Alt или Shift превратили бы Ctrl+V в другое сочетание.
                for (int i = 0; i < 40 && HeldModifiers().Count > 0; i++)
                    await Task.Delay(10);

                var keys = new List<(ushort Key, bool Up)>();
                foreach (ushort vk in HeldModifiers())
                    keys.Add((vk, true));
                bool ctrlHeld = KeyboardHook.IsDown(VK_CONTROL);
                if (!ctrlHeld)
                    keys.Add((VK_CONTROL, false));
                keys.Add((VK_V, false));
                keys.Add((VK_V, true));
                if (!ctrlHeld)
                    keys.Add((VK_CONTROL, true));
                KeyboardHook.SendKeys(keys.ToArray());
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }

        static List<ushort> HeldModifiers()
        {
            var held = new List<ushort>();
            foreach (ushort vk in StuckModifiers)
            {
                if (KeyboardHook.IsDown(vk))
                    held.Add(vk);
            }
            return held;
        }
    }
}
