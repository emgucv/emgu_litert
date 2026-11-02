//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// How much video memory this process can still use, to decide how much of a model goes to the GPU (ZImageTurbo).
    /// Windows only (DXGI 1.4: IDXGIAdapter3.QueryVideoMemoryInfo of the hardware adapter with the most dedicated memory, the
    /// one LiteRT's WebGPU accelerator picks on a PC with a discrete GPU); elsewhere the amount isn't known.
    /// </summary>
    internal static class GpuMemory
    {
        /// <summary>
        /// The video memory the system lets this process still allocate: its budget less what it uses (a model already
        /// loaded on the GPU in this process counts, other programs' share is out of the budget).
        /// </summary>
        /// <returns>The bytes, or null when they can't be told</returns>
        internal static long? AvailableBytes()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;
            try
            {
                return QueryDxgi();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static long? QueryDxgi()
        {
            Guid factoryId = typeof(IDXGIFactory1).GUID;
            object factoryObject;
            if (CreateDXGIFactory1(ref factoryId, out factoryObject) < 0 || factoryObject == null)
                return null;
            IDXGIFactory1 factory = (IDXGIFactory1)factoryObject;
            IDXGIAdapter1 best = null;
            try
            {
                ulong bestMemory = 0;
                for (uint index = 0; ; index++)
                {
                    IDXGIAdapter1 adapter;
                    if (factory.EnumAdapters1(index, out adapter) < 0 || adapter == null)
                        break;
                    DXGI_ADAPTER_DESC1 description;
                    bool better = false;
                    if (adapter.GetDesc1(out description) >= 0)
                    {
                        bool software = (description.Flags & DxgiAdapterFlagSoftware) != 0;
                        ulong memory = description.DedicatedVideoMemory.ToUInt64();
                        better = !software && memory > bestMemory;
                        if (better)
                            bestMemory = memory;
                    }
                    if (better)
                    {
                        if (best != null)
                            Marshal.ReleaseComObject(best);
                        best = adapter;
                    }
                    else
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
                IDXGIAdapter3 adapter3 = best as IDXGIAdapter3;
                if (adapter3 == null)
                    return null;
                DXGI_QUERY_VIDEO_MEMORY_INFO info;
                if (adapter3.QueryVideoMemoryInfo(0, DxgiMemorySegmentGroupLocal, out info) < 0)
                    return null;
                ulong free = info.Budget > info.CurrentUsage ? info.Budget - info.CurrentUsage : 0;
                return free > long.MaxValue ? long.MaxValue : (long)free;
            }
            finally
            {
                if (best != null)
                    Marshal.ReleaseComObject(best);
                Marshal.ReleaseComObject(factory);
            }
        }

        private const uint DxgiAdapterFlagSoftware = 2;
        private const int DxgiMemorySegmentGroupLocal = 0;

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DXGI_QUERY_VIDEO_MEMORY_INFO
        {
            public ulong Budget;
            public ulong CurrentUsage;
            public ulong AvailableForReservation;
            public ulong CurrentReservation;
        }

        // Only the methods used are declared with their parameters; the others are placeholders that keep the vtable slots
        // in order (an interface and the ones it inherits are one flat list for COM).
        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            // IDXGIObject
            [PreserveSig] int SetPrivateData();
            [PreserveSig] int SetPrivateDataInterface();
            [PreserveSig] int GetPrivateData();
            [PreserveSig] int GetParent();
            // IDXGIFactory
            [PreserveSig] int EnumAdapters();
            [PreserveSig] int MakeWindowAssociation();
            [PreserveSig] int GetWindowAssociation();
            [PreserveSig] int CreateSwapChain();
            [PreserveSig] int CreateSoftwareAdapter();
            // IDXGIFactory1
            [PreserveSig] int EnumAdapters1(uint adapter, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 adapterOut);
        }

        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter1
        {
            // IDXGIObject
            [PreserveSig] int SetPrivateData();
            [PreserveSig] int SetPrivateDataInterface();
            [PreserveSig] int GetPrivateData();
            [PreserveSig] int GetParent();
            // IDXGIAdapter
            [PreserveSig] int EnumOutputs();
            [PreserveSig] int GetDesc();
            [PreserveSig] int CheckInterfaceSupport();
            // IDXGIAdapter1
            [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 description);
        }

        [ComImport, Guid("645967A4-1392-4310-A798-8053CE3E93FD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter3
        {
            // IDXGIObject
            [PreserveSig] int SetPrivateData();
            [PreserveSig] int SetPrivateDataInterface();
            [PreserveSig] int GetPrivateData();
            [PreserveSig] int GetParent();
            // IDXGIAdapter
            [PreserveSig] int EnumOutputs();
            [PreserveSig] int GetDesc();
            [PreserveSig] int CheckInterfaceSupport();
            // IDXGIAdapter1, IDXGIAdapter2
            [PreserveSig] int GetDesc1();
            [PreserveSig] int GetDesc2();
            // IDXGIAdapter3
            [PreserveSig] int RegisterHardwareContentProtectionTeardownStatusEvent();
            [PreserveSig] int UnregisterHardwareContentProtectionTeardownStatus();
            [PreserveSig] int QueryVideoMemoryInfo(uint nodeIndex, int segmentGroup, out DXGI_QUERY_VIDEO_MEMORY_INFO info);
        }
    }
}
