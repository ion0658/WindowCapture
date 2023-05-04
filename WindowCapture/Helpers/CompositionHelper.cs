using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace WindowCapture.Helpers {
    internal static class CompositionHelper {
        [ComImport]
        [Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface ICompositorInterop {
            IntPtr CreateCompositionSurfaceForHandle(
                IntPtr swapChain);

            IntPtr CreateCompositionSurfaceForSwapChain(
                IntPtr swapChain);

            IntPtr CreateGraphicsDevice(
                IntPtr renderingDevice);
        }

        [ComImport]
        [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface ICompositorDesktopInterop {
            IntPtr CreateDesktopWindowTarget(
                IntPtr hwnd,
                bool isTopmost);
        }

        public static CompositionTarget CreateDesktopWindowTarget(this Compositor compositor, IntPtr hwnd, bool isTopmost) {
            var desktopInterop = compositor.As<ICompositorDesktopInterop>();
            var itemPointer = desktopInterop.CreateDesktopWindowTarget(hwnd, isTopmost);
            var item = MarshalGeneric<DesktopWindowTarget>.FromAbi(itemPointer);
            return item.As<CompositionTarget>();
        }

        public static ICompositionSurface CreateCompositionSurfaceForSwapChain(this Compositor compositor, SharpDX.DXGI.SwapChain1 swapChain) {
            var interop = compositor.As<ICompositorInterop>();
            var itemPointer = interop.CreateCompositionSurfaceForSwapChain(swapChain.NativePointer);
            var item = MarshalInterface<ICompositionSurface>.FromAbi(itemPointer);
            return item;

        }
    }
}
