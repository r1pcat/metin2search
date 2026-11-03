using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Metin2Search.Local;

public sealed partial class ExternalReader : IDisposable, IMarketClient
{
    private readonly SafeProcessHandle handle;
    private readonly Process process;
    private readonly ReaderProfile profile;
    private readonly ulong relocation;
    public int Pid => process.Id;
    public string? ExecutableHash { get; }
    public bool CanMonitor => NativeLayoutValidated;

    public ExternalReader(int pid, ReaderProfile profile)
    {
        profile.Validate();
        this.profile = profile;
        process = Process.GetProcessById(pid);
        handle = OpenProcess(0x1000 | 0x0010, false, pid);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose(); process.Dispose();
            throw new Win32Exception(error, $"PID {pid}: salt okunur bağlantı açılamadı ({error}). DLL veya bellek yazma denenmedi.");
        }
        try
        {
            if (!IsWow64Process2(handle, out var machine, out _) || machine != 0x014c)
                throw new InvalidOperationException("Bu okuyucu profili yalnızca x86/32 bit istemci içindir.");
            if (!string.IsNullOrWhiteSpace(profile.ModuleName))
            {
                var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.ModuleName, profile.ModuleName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Profildeki modül süreçte bulunamadı.");
                var actual = unchecked((ulong)module.BaseAddress.ToInt64());
                var dump = ReaderProfile.Address(profile.DumpImageBase);
                relocation = unchecked(actual - dump);
            }
            if (!string.IsNullOrWhiteSpace(profile.ExpectedExecutableSha256))
            {
                var path = new System.Text.StringBuilder(32768);
                var length = path.Capacity;
                if (!QueryFullProcessImageName(handle, 0, path, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using var file = File.OpenRead(path.ToString());
                ExecutableHash = Convert.ToHexString(SHA256.HashData(file));
                if (!ExecutableHash.Equals(profile.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Çalışan EXE hash'i profil ile eşleşmiyor. Dump hash'i orijinal EXE hash'i olmayabilir.");
            }
            var manager = Pointer();
            _ = Read(manager, 0x44dd);
            NativeLayoutValidated = VerifyNativeLayout();
        }
        catch { Dispose(); throw; }
    }

    public async Task<MarketCapture> CaptureAsync(string server, string map, string label, bool monitored, CancellationToken token)
    {
        if (NativeLayoutValidated) return await CaptureNativeAsync(server, map, label, token);
        if (monitored && !CanMonitor) throw new InvalidOperationException("Sürekli okuma için EXE hash'i, kanal, açık-pazar, VID ve açılış nesli adresleri doğrulanmalı.");
        token.ThrowIfCancellationRequested();
        var guard = Guard(monitored);
        var pointer = Pointer();
        var before = Read(pointer, 0x44dd);
        await Task.Delay(120, token);
        byte[] after;
        try { after = Read(pointer, 0x44dd); }
        catch (MarketReadException error)
        {
            throw new MarketReadException(error.Message, [new(pointer, before.Length, before, "first-snapshot"), .. error.Diagnostics]);
        }
        if (pointer != Pointer() || !before.AsSpan().SequenceEqual(after) || guard != Guard(monitored))
            throw new MarketReadException("Pazar okuma sırasında değişti. Ham veriler tanı kaydı olarak korunacak; ilan sayılmayacak.",
                new(pointer, before.Length, before, "first-snapshot"), new(pointer, after.Length, after, "second-snapshot"));
        var tabs = after[0x40];
        if (tabs is < 1 or > 3) throw new MarketReadException("Sekme sayısı geçersiz; profil/adres eşleşmesi doğrulanmalı.", new ReadDiagnostic(pointer, after.Length, after, "invalid-tab-count"));
        var slots = new List<SlotData>();
        for (var tab = 0; tab < tabs; tab++) for (var slot = 0; slot < 40; slot++)
            slots.Add(new SlotData(tab, slot, after.AsSpan(0x60 + tab * 0x141c + slot * 0x80, 0x80).ToArray()));
        var warnings = new List<string>();
        if (!monitored) warnings.Add("Manuel test: kanal, açık pazar, pazar kimliği ve açılış nesli birlikte doğrulanmadı. Eski tampon olabilir.");
        if (!profile.PricesVerified) warnings.Add("Çalışan istemcinin metot tablosu eşleşmedi; fiyat alanları bu sürüm için doğrulanmadı.");
        warnings.Add("Eşya adları ve ek alanların anlamları için sürüme uygun tanım tablosu henüz eklenmedi.");
        return new MarketCapture
        {
            Source = monitored ? "Live" : "LiveUnverified", Server = server, Map = map,
            ShopLabel = label, ShopVid = monitored ? guard.Vid.ToString() : null,
            ChannelVerified = monitored, IsComplete = monitored, ProfileVersion = profile.Version,
            ClientHash = ExecutableHash, PricesVerified = profile.PricesVerified, RawManager = after, Slots = slots,
            Warnings = warnings.ToArray()
        };
    }

    private (uint Channel, byte Open, uint Vid, uint Generation) Guard(bool required)
    {
        if (!required) return default;
        var channel = U32(profile.ChannelAddress!);
        if (channel != 1) throw new InvalidOperationException($"Kanal 1 gerekli. Okunan kanal: {channel}. Tarama durduruldu.");
        var open = Read(Resolve(profile.ShopOpenAddress!), 1)[0];
        if (open != 1) throw new InvalidOperationException("Doğrulanmış açık pazar bulunamadı. Tarama durduruldu.");
        var vid = U32(profile.ShopVidAddress!);
        if (vid == 0) throw new InvalidDataException("Pazar VID sıfır; kayıt reddedildi.");
        return (channel, open, vid, U32(profile.OpenGenerationAddress!));
    }
    private ulong Resolve(string address) => unchecked(ReaderProfile.Address(address) + relocation);
    private uint U32(string address) => ItemDecoder.U32(Read(Resolve(address), 4), 0);
    private ulong Pointer()
    {
        var pointer = U32(profile.ManagerPointerAddress);
        if (pointer < 0x10000 || pointer > uint.MaxValue - 0x44dd)
            throw new InvalidDataException("Pazar nesnesi işaretçisi geçersiz veya istemci henüz hazır değil.");
        return pointer;
    }
    private byte[] Read(ulong address, int length)
    {
        if (process.HasExited) throw new InvalidOperationException("İstemci kapandı. Tarama durduruldu.");
        var bytes = new byte[length];
        if (!ReadProcessMemory(handle, new IntPtr(checked((long)address)), bytes, (nuint)length, out var read) || read != (nuint)length)
        {
            var error = Marshal.GetLastWin32Error();
            var partial = bytes.AsSpan(0, (int)Math.Min((ulong)read, (ulong)length)).ToArray();
            throw new MarketReadException($"0x{address:X}: {length} bayt okunamadı (Windows {error}); kısmi veri ilan olarak kabul edilmedi.",
                new ReadDiagnostic(address, length, partial, "read-failure"));
        }
        return bytes;
    }
    public void Dispose() { handle.Dispose(); process.Dispose(); }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle handle, IntPtr address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process2(SafeProcessHandle handle, out ushort processMachine, out ushort nativeMachine);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, System.Text.StringBuilder name, ref int size);
}
