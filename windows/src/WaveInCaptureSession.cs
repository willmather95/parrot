using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Parrot.Windows
{
    internal sealed class CaptureBufferOrder
    {
        private readonly int _count;
        private int _next;

        internal CaptureBufferOrder(int count) : this(count, 0) { }

        internal CaptureBufferOrder(int count, int first)
        {
            if (count <= 0 || first < 0 || first >= count)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            _count = count;
            _next = first;
        }

        internal bool TryTake(bool[] completed, out int index)
        {
            if (completed == null || completed.Length != _count)
            {
                throw new ArgumentException("Completion map has the wrong size.", "completed");
            }
            if (!completed[_next])
            {
                index = -1;
                return false;
            }
            index = _next;
            completed[index] = false;
            _next = (_next + 1) % _count;
            return true;
        }
    }

    internal sealed class WaveInCaptureSession : IDisposable
    {
        internal const int SampleRate = 16000;
        internal const int MaximumSeconds = 10 * 60;

        private const uint WaveMapper = 0xFFFFFFFF;
        private const uint CallbackEvent = 0x00050000;
        private const uint WaveHeaderDone = 0x00000001;
        private const int BufferMilliseconds = 100;
        private const int BufferCount = 8;

        private readonly object _stopLock = new object();
        private readonly object _nativeLock = new object();
        private readonly SynchronizationContext _uiContext;
        private readonly Action<long> _limitReached;
        private readonly Action<long, Exception> _captureFailed;
        private readonly List<float> _samples = new List<float>(SampleRate * 60);
        private readonly AutoResetEvent _bufferEvent = new AutoResetEvent(false);
        private readonly List<BufferSlot> _buffers = new List<BufferSlot>();
        private readonly CaptureBufferOrder _bufferOrder = new CaptureBufferOrder(BufferCount);
        private IntPtr _waveIn;
        private Thread _worker;
        private volatile bool _accepting;
        private bool _limitNotified;
        private bool _failureNotified;
        private bool _stopRequested;
        private bool _stopCompleted;
        private bool _disposed;
        private Exception _workerError;

        internal WaveInCaptureSession(
            long generation,
            SynchronizationContext uiContext,
            Action<long> limitReached,
            Action<long, Exception> captureFailed)
        {
            Generation = generation;
            _uiContext = uiContext;
            _limitReached = limitReached;
            _captureFailed = captureFailed;
        }

        internal long Generation { get; private set; }

        internal void Start()
        {
            ThrowIfDisposed();
            WaveFormat format = WaveFormat.CreatePcm16Mono(SampleRate);
            int result = WaveInOpen(
                out _waveIn,
                WaveMapper,
                ref format,
                _bufferEvent.SafeWaitHandle.DangerousGetHandle(),
                IntPtr.Zero,
                CallbackEvent);
            ThrowOnWaveError(result, "open the default microphone");

            try
            {
                for (int index = 0; index < BufferCount; index++)
                {
                    BufferSlot slot = new BufferSlot(BufferByteCount());
                    _buffers.Add(slot);
                    result = WaveInPrepareHeader(
                        _waveIn,
                        slot.HeaderPointer,
                        (uint)Marshal.SizeOf(typeof(WaveHeader)));
                    ThrowOnWaveError(result, "prepare a microphone buffer");
                    slot.Prepared = true;
                    result = WaveInAddBuffer(
                        _waveIn,
                        slot.HeaderPointer,
                        (uint)Marshal.SizeOf(typeof(WaveHeader)));
                    ThrowOnWaveError(result, "queue a microphone buffer");
                }

                _accepting = true;
                _worker = new Thread(BufferLoop);
                _worker.IsBackground = true;
                _worker.Name = "Parrot microphone capture";
                _worker.Start();
                lock (_nativeLock)
                {
                    result = WaveInStart(_waveIn);
                }
                ThrowOnWaveError(result, "start microphone capture");
            }
            catch
            {
                RequestNativeStop();
                _bufferEvent.Set();
                if (_worker != null) { _worker.Join(2000); }
                ReleaseNativeResources();
                throw;
            }
        }

        internal float[] StopAndGetSamples()
        {
            lock (_stopLock)
            {
                if (_stopCompleted)
                {
                    return _samples.ToArray();
                }
                if (!_stopRequested)
                {
                    _stopRequested = true;
                    RequestNativeStop();
                    _bufferEvent.Set();
                }
                if (_worker != null && !_worker.Join(4000))
                {
                    throw new TimeoutException("Microphone buffers did not return within four seconds.");
                }
                if (!ReleaseNativeResources())
                {
                    throw new InvalidOperationException(
                        "Windows did not release the microphone buffers safely. Restart Parrot before recording again.");
                }
                if (_workerError != null)
                {
                    throw new InvalidOperationException("Microphone capture failed.", _workerError);
                }
                _stopCompleted = true;
                return _samples.ToArray();
            }
        }

        internal static void ProbeDefaultMicrophone()
        {
            IntPtr handle;
            WaveFormat format = WaveFormat.CreatePcm16Mono(SampleRate);
            int result = WaveInOpen(
                out handle,
                WaveMapper,
                ref format,
                IntPtr.Zero,
                IntPtr.Zero,
                0);
            ThrowOnWaveError(result, "initialize the default microphone");
            if (handle != IntPtr.Zero)
            {
                result = WaveInClose(handle);
                ThrowOnWaveError(result, "release the default microphone probe");
            }
        }

        private void BufferLoop()
        {
            try
            {
                while (true)
                {
                    _bufferEvent.WaitOne();
                    bool[] completed = SnapshotCompletedBuffers();
                    int index;
                    while (_bufferOrder.TryTake(completed, out index))
                    {
                        BufferSlot slot = _buffers[index];
                        WaveHeader header = ReadHeader(slot);
                        if (header.BytesRecorded > 0)
                        {
                            CopySamples(header.Data, (int)header.BytesRecorded);
                        }

                        lock (_nativeLock)
                        {
                            if (_accepting && _samples.Count < SampleRate * MaximumSeconds)
                            {
                                int result = WaveInAddBuffer(
                                    _waveIn,
                                    slot.HeaderPointer,
                                    (uint)Marshal.SizeOf(typeof(WaveHeader)));
                                ThrowOnWaveError(result, "requeue a microphone buffer");
                            }
                            else
                            {
                                slot.Returned = true;
                            }
                        }
                    }

                    if (_samples.Count >= SampleRate * MaximumSeconds && _accepting)
                    {
                        RequestNativeStop();
                        _bufferEvent.Set();
                        NotifyLimitReached();
                    }
                    if (!_accepting && AllBuffersReturned())
                    {
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                _workerError = exception;
                RequestNativeStop();
                NotifyCaptureFailed(exception);
            }
        }

        private bool[] SnapshotCompletedBuffers()
        {
            bool[] completed = new bool[_buffers.Count];
            lock (_nativeLock)
            {
                for (int index = 0; index < _buffers.Count; index++)
                {
                    BufferSlot slot = _buffers[index];
                    if (!slot.Returned)
                    {
                        WaveHeader header = ReadHeader(slot);
                        completed[index] = (header.Flags & WaveHeaderDone) != 0;
                    }
                }
            }
            return completed;
        }

        private static WaveHeader ReadHeader(BufferSlot slot)
        {
            return (WaveHeader)Marshal.PtrToStructure(slot.HeaderPointer, typeof(WaveHeader));
        }

        private void RequestNativeStop()
        {
            lock (_nativeLock)
            {
                _accepting = false;
                if (_waveIn != IntPtr.Zero)
                {
                    WaveInStop(_waveIn);
                    WaveInReset(_waveIn);
                }
            }
        }

        private bool AllBuffersReturned()
        {
            foreach (BufferSlot slot in _buffers)
            {
                if (!slot.Returned) { return false; }
            }
            return true;
        }

        private void CopySamples(IntPtr data, int byteCount)
        {
            int remainingSamples = SampleRate * MaximumSeconds - _samples.Count;
            int sampleCount = Math.Min(byteCount / 2, remainingSamples);
            if (sampleCount <= 0) { return; }
            byte[] bytes = new byte[sampleCount * 2];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            for (int index = 0; index < bytes.Length; index += 2)
            {
                short value = (short)(bytes[index] | (bytes[index + 1] << 8));
                _samples.Add(value / 32768F);
            }
        }

        private void NotifyLimitReached()
        {
            if (_limitNotified) { return; }
            _limitNotified = true;
            _uiContext.Post(delegate(object ignored) { _limitReached(Generation); }, null);
        }

        private void NotifyCaptureFailed(Exception exception)
        {
            if (_failureNotified) { return; }
            _failureNotified = true;
            _uiContext.Post(
                delegate(object ignored) { _captureFailed(Generation, exception); },
                null);
        }

        private bool ReleaseNativeResources()
        {
            if (_worker != null && _worker.IsAlive) { return false; }
            if (_waveIn != IntPtr.Zero)
            {
                bool allUnprepared = true;
                foreach (BufferSlot slot in _buffers)
                {
                    if (slot.Prepared)
                    {
                        int result = WaveInUnprepareHeader(
                            _waveIn,
                            slot.HeaderPointer,
                            (uint)Marshal.SizeOf(typeof(WaveHeader)));
                        if (result == 0) { slot.Prepared = false; }
                        else { allUnprepared = false; }
                    }
                }
                if (!allUnprepared || WaveInClose(_waveIn) != 0)
                {
                    return false;
                }
                _waveIn = IntPtr.Zero;
            }
            foreach (BufferSlot slot in _buffers) { slot.Dispose(); }
            _buffers.Clear();
            return true;
        }

        private static int BufferByteCount()
        {
            return SampleRate * 2 * BufferMilliseconds / 1000;
        }

        private static void ThrowOnWaveError(int result, string operation)
        {
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Windows could not " + operation + " (waveIn error " + result + ").");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) { throw new ObjectDisposedException("WaveInCaptureSession"); }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            if (!_stopCompleted)
            {
                try { StopAndGetSamples(); }
                catch (Exception) { ReleaseNativeResources(); }
            }
            if ((_worker == null || !_worker.IsAlive) && _waveIn == IntPtr.Zero)
            {
                _bufferEvent.Dispose();
            }
        }

        private sealed class BufferSlot : IDisposable
        {
            private GCHandle _dataHandle;

            internal BufferSlot(int byteCount)
            {
                byte[] data = new byte[byteCount];
                _dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                WaveHeader header = new WaveHeader();
                header.Data = _dataHandle.AddrOfPinnedObject();
                header.BufferLength = (uint)byteCount;
                HeaderPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
                Marshal.StructureToPtr(header, HeaderPointer, false);
            }

            internal IntPtr HeaderPointer { get; private set; }
            internal bool Prepared { get; set; }
            internal bool Returned { get; set; }

            public void Dispose()
            {
                if (HeaderPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(HeaderPointer);
                    HeaderPointer = IntPtr.Zero;
                }
                if (_dataHandle.IsAllocated) { _dataHandle.Free(); }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat
        {
            internal ushort FormatTag;
            internal ushort Channels;
            internal uint SamplesPerSecond;
            internal uint AverageBytesPerSecond;
            internal ushort BlockAlign;
            internal ushort BitsPerSample;
            internal ushort ExtraSize;

            internal static WaveFormat CreatePcm16Mono(int sampleRate)
            {
                WaveFormat format = new WaveFormat();
                format.FormatTag = 1;
                format.Channels = 1;
                format.SamplesPerSecond = (uint)sampleRate;
                format.AverageBytesPerSecond = (uint)(sampleRate * 2);
                format.BlockAlign = 2;
                format.BitsPerSample = 16;
                return format;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            internal IntPtr Data;
            internal uint BufferLength;
            internal uint BytesRecorded;
            internal IntPtr User;
            internal uint Flags;
            internal uint Loops;
            internal IntPtr Next;
            internal IntPtr Reserved;
        }

        [DllImport("winmm.dll", EntryPoint = "waveInOpen")]
        private static extern int WaveInOpen(
            out IntPtr waveIn,
            uint deviceId,
            ref WaveFormat format,
            IntPtr callback,
            IntPtr instance,
            uint flags);

        [DllImport("winmm.dll", EntryPoint = "waveInPrepareHeader")]
        private static extern int WaveInPrepareHeader(
            IntPtr waveIn,
            IntPtr header,
            uint size);

        [DllImport("winmm.dll", EntryPoint = "waveInUnprepareHeader")]
        private static extern int WaveInUnprepareHeader(
            IntPtr waveIn,
            IntPtr header,
            uint size);

        [DllImport("winmm.dll", EntryPoint = "waveInAddBuffer")]
        private static extern int WaveInAddBuffer(IntPtr waveIn, IntPtr header, uint size);

        [DllImport("winmm.dll", EntryPoint = "waveInStart")]
        private static extern int WaveInStart(IntPtr waveIn);

        [DllImport("winmm.dll", EntryPoint = "waveInStop")]
        private static extern int WaveInStop(IntPtr waveIn);

        [DllImport("winmm.dll", EntryPoint = "waveInReset")]
        private static extern int WaveInReset(IntPtr waveIn);

        [DllImport("winmm.dll", EntryPoint = "waveInClose")]
        private static extern int WaveInClose(IntPtr waveIn);
    }
}
