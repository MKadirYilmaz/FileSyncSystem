using System;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

class Program
{

    static int affectedFiles = 0;
    static float transferInMB = 0;
    static float deleteInMB = 0;

    static async Task Main(string[] args)
    {
        string version = string.Empty;
        if(args.Length > 1 && args[0] == "--version")
        {
            Console.WriteLine("Version Control System");
            version = args[1];
        }
        while (true)
        {
            affectedFiles = 0;
            transferInMB = 0;
            deleteInMB = 0;
            Console.WriteLine("Kaynak dizin yolunu girin: ");
            string sourceDir = Console.ReadLine();

            Console.WriteLine("Hedef dizin yolunu girin: ");
            string targetDir = Console.ReadLine();
            targetDir = Path.Combine(targetDir, version);

            if (!Directory.Exists(sourceDir))
            {
                Console.WriteLine("Kaynak dizin mevcut değil.");
                return;
            }
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            await SyncDirectories(sourceDir, targetDir);
            stopwatch.Stop();
            
            TimeSpan timeSpan = stopwatch.Elapsed;
            Console.WriteLine($"Senkronizasyon tamamlandı. {affectedFiles} dosya etkilendi. \nToplam Sure: {timeSpan.TotalSeconds} saniye. \n" +
                $"Transfer edilen dosya boyutu: {transferInMB} MB \nSilinen dosya boyutu: {deleteInMB} MB");
        }
    }
    static void SlowSyncDirectories(string sourceDir, string targetDir)
    {
        foreach (string targetPath in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(targetDir, targetPath);
            string sourcePath = Path.Combine(sourceDir, relativePath);

            Console.WriteLine($"{targetPath} kontrol ediliyor... (Fazlalik)");

            if (!File.Exists(sourcePath))
            {
                Console.WriteLine($"Farklilik tespit edildi siliniyor: {targetPath}");
                FileInfo fileInfo = new FileInfo(targetPath);
                deleteInMB = (fileInfo.Length / 1024) / 1024;
                File.Delete(targetPath);
                Console.WriteLine($"Silindi: {targetPath}");
                affectedFiles++;
            }
        }
        foreach (string sourcePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, sourcePath);
            string targetPath = Path.Combine(targetDir, relativePath);

            Console.WriteLine($"{targetPath} kontrol ediliyor... (Var olma ve dogruluk)");

            if (!File.Exists(targetPath) || !FilesAreIdentical(sourcePath, targetPath))
            {
                Console.WriteLine($"Farklilik tespit edildi kopyalaniyor: {sourcePath}");
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
                File.Copy(sourcePath, targetPath, true);
                Console.WriteLine($"Kopyalandı: {sourcePath} -> {targetPath}");
                FileInfo fileInfo = new FileInfo(targetPath);
                transferInMB += (fileInfo.Length / 1024) / 1024;
                affectedFiles++;
            }
        }
    }
    
    static async Task SyncDirectories(string sourceDir, string targetDir)
    {
        bool secondPhase = false;
        SemaphoreSlim semaphore = new SemaphoreSlim(256);
        foreach (string targetPath in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
        {
            await semaphore.WaitAsync();
            _ = Task.Run(() =>
            {
                try
                {
                    string relativePath = Path.GetRelativePath(targetDir, targetPath);
                    string sourcePath = Path.Combine(sourceDir, relativePath);

                    Console.WriteLine($"{targetPath} kontrol ediliyor... (Fazlalik)");

                    if (!File.Exists(sourcePath))
                    {
                        Console.WriteLine($"Farklilik tespit edildi siliniyor: {targetPath}");
                        FileInfo fileInfo = new FileInfo(targetPath);
                        deleteInMB = (fileInfo.Length / 1024) / 1024;
                        File.Delete(targetPath);
                        Console.WriteLine($"Silindi: {targetPath}");
                        affectedFiles++;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }
        while (!secondPhase)
        {
            if (semaphore.CurrentCount == 256)
                secondPhase = true;
        }
        bool exit = false;
        foreach (string sourcePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            await semaphore.WaitAsync();
            _ = Task.Run(() =>
            {
                try
                {
                    string relativePath = Path.GetRelativePath(sourceDir, sourcePath);
                    string targetPath = Path.Combine(targetDir, relativePath);

                    Console.WriteLine($"{targetPath} kontrol ediliyor... (Var olma ve dogruluk)");

                    if (!File.Exists(targetPath) || !FilesAreIdentical(sourcePath, targetPath))
                    {
                        Console.WriteLine($"Farklilik tespit edildi kopyalaniyor: {sourcePath}");
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
                        File.Copy(sourcePath, targetPath, true);
                        Console.WriteLine($"Kopyalandı: {sourcePath} -> {targetPath}");
                        FileInfo fileInfo = new FileInfo(targetPath);
                        transferInMB += (fileInfo.Length / 1024) / 1024;
                        affectedFiles++;
                    }
                }
                finally
                { 
                    semaphore.Release();
                }
            });
        }
        while (!exit)
        {
            if (semaphore.CurrentCount == 256)
                exit = true;
        }
    }

    static void HandleDelete(string sourceDir, string targetDir, string targetPath)
    {
        string relativePath = Path.GetRelativePath(targetDir, targetPath);
        string sourcePath = Path.Combine(sourceDir, relativePath);

        Console.WriteLine($"{targetPath} kontrol ediliyor... (Fazlalik)");

        if (!File.Exists(sourcePath))
        {
            Console.WriteLine($"Farklilik tespit edildi siliniyor: {targetPath}");
            FileInfo fileInfo = new FileInfo(targetPath);
            deleteInMB = (fileInfo.Length / 1024) / 1024;
            File.Delete(targetPath);
            Console.WriteLine($"Silindi: {targetPath}");
            affectedFiles++;
        }
    }
    
    static void HandleCopying(string sourceDir, string targetDir, string sourcePath)
    {
        string relativePath = Path.GetRelativePath(sourceDir, sourcePath);
        string targetPath = Path.Combine(targetDir, relativePath);

        Console.WriteLine($"{targetPath} kontrol ediliyor... (Var olma ve dogruluk)");

        if (!File.Exists(targetPath) || !FilesAreIdentical(sourcePath, targetPath))
        {
            Console.WriteLine($"Farklilik tespit edildi kopyalaniyor: {sourcePath}");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
            File.Copy(sourcePath, targetPath, true);
            Console.WriteLine($"Kopyalandı: {sourcePath} -> {targetPath}");
            FileInfo fileInfo = new FileInfo(targetPath);
            transferInMB += (fileInfo.Length / 1024) / 1024;
            affectedFiles++;
        }
    }
    
    static bool FilesAreIdentical(string file1, string file2)
    {
        FileInfo info1 = new FileInfo(file1);
        FileInfo info2 = new FileInfo(file2);
        if (info1.Length != info2.Length || info1.LastWriteTime != info2.LastWriteTime)
        {
            Console.WriteLine("Dosyada bir seyler degismis olabilir...");
            try
            {
                string hash1 = ComputeFileHash(file1);
                string hash2 = ComputeFileHash(file2);

                bool equal = hash1 == hash2;
                if (!equal)
                    Console.WriteLine("Dosya icerigi degistirilmis...");
                return equal;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hata: {ex.Message}");
                return false;
            }
        }
        else
        {
            Console.WriteLine("Dosya boyutu veya son yazim tarihi degismemis...");
            return true;
        }

        
    }

    static string ComputeFileHash(string filePath)
    {
        using MD5 md5 = MD5.Create();
        using FileStream stream = File.OpenRead(filePath);
        byte[] hashBytes = md5.ComputeHash(stream);

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}