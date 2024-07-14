using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace GalaxyBudsClient.Platform.Windows
{
    public partial class WndProcClient : IDisposable
    {
        public event EventHandler<WindowMessage>? MessageReceived;

        private readonly IntPtr _hwnd;
        /* prevent garbage collection */
        private readonly Unmanaged.WNDCLASSEX _wndClassEx;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task? _messageProcessingTask;

        public IntPtr WindowHandle => _hwnd;

        public WndProcClient()
        {
            _wndClassEx = new Unmanaged.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Unmanaged.WNDCLASSEX>(),
                lpfnWndProc = delegate (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
                    var message = new WindowMessage()
                    {
                        hWnd = hWnd,
                        Msg = (WindowsMessage)msg,
                        wParam = wParam,
                        lParam = lParam
                    };

                    MessageReceived?.Invoke(this, message);

                    return Unmanaged.DefWindowProc(hWnd, msg, wParam, lParam);
                },
                hInstance = Unmanaged.GetModuleHandle(null),
                lpszClassName = "MessageWindow " + Guid.NewGuid(),
            };

            ushort atom = Unmanaged.RegisterClassEx(ref _wndClassEx);

            if (atom == 0)
            {
                Log.Error("Interop.Win32.WndProcClient: atom is null");
                throw new Win32Exception();
            }

            _hwnd = Unmanaged.CreateWindowEx(0, atom, null, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, Unmanaged.GetModuleHandle(null), IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                Log.Error("Interop.Win32.WndProcClient: nWnd is null");
                throw new Win32Exception();
            }

            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void ProcessMessage()
        {
            if (Unmanaged.GetMessage(out var msg, IntPtr.Zero, 0, 0) > -1)
            {
                Unmanaged.TranslateMessage(ref msg);
                Unmanaged.DispatchMessage(ref msg);
            }
            else
            {
                Log.Error("WndProcClient: Unmanaged error in {This}. Error Code: {Code}", nameof(ProcessMessage),
                    Marshal.GetLastWin32Error());
            }
        }

        public void RunLoop(CancellationToken cancellationToken)
        {
            _messageProcessingTask = Task.Run(() =>
            {
                var result = 0;
                while (!cancellationToken.IsCancellationRequested
                       && (result = Unmanaged.GetMessage(out var msg, IntPtr.Zero, 0, 0)) > 0)
                {
                    Unmanaged.TranslateMessage(ref msg);
                    Unmanaged.DispatchMessage(ref msg);
                    Thread.Sleep(10);
                }
                if (result < 0)
                {
                    Log.Error("WndProcClient: Unmanaged error in {This}. Error Code: {Code}", nameof(RunLoop),
                        Marshal.GetLastWin32Error());
                }
            }, cancellationToken);
        }

        public void Start()
        {
            if (_messageProcessingTask == null || _messageProcessingTask.IsCompleted)
            {
                RunLoop(_cancellationTokenSource.Token);
            }
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            _messageProcessingTask?.Wait();
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource.Dispose();
            if (_hwnd != IntPtr.Zero)
            {
                Unmanaged.DestroyWindow(_hwnd);
            }
        }
    }
}