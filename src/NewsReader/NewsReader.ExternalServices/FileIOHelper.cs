﻿using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Windows.Storage;

namespace Jbe.NewsReader.ExternalServices
{
    internal static class FileIOHelper
    {
        public static string GetArchiveFileName(string fileName)
        {
            return fileName + ".zip";
        }

        public static T LoadCompressed<T>(Stream archiveStream, string fileName) where T : class
        {
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entry = archive.GetEntry(fileName);
                using (var stream = entry.Open())
                {
                    var serializer = new DataContractSerializer(typeof(T));
                    return (T)serializer.ReadObject(stream);
                }
            }
        }

        public static async Task<T> LoadCompressedAsync<T>(StorageFolder folder, string fileName) where T : class
        {
            if (folder == null) { throw new ArgumentNullException(nameof(folder)); }
            if (string.IsNullOrEmpty(fileName)) { throw new ArgumentException("String must not be null or empty.", nameof(fileName)); }

            try
            {
                using (var archiveStream = await folder.OpenStreamForReadAsync(GetArchiveFileName(fileName)))
                {
                    return LoadCompressed<T>(archiveStream, fileName);
                }
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        public static async Task SaveCompressedAsync(object data, StorageFolder folder, string fileName)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (folder == null) { throw new ArgumentNullException(nameof(folder)); }
            if (string.IsNullOrEmpty(fileName)) { throw new ArgumentException("String must not be null or empty.", nameof(fileName)); }

            using (var archiveStream = await folder.OpenStreamForWriteAsync(GetArchiveFileName(fileName), CreationCollisionOption.ReplaceExisting))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                using (var stream = entry.Open())
                {
                    var serializer = new DataContractSerializer(data.GetType());
                    serializer.WriteObject(stream, data);
                    await stream.FlushAsync();
                }
            }
        }

        public static async Task MigrateDataAsync(StorageFolder folder, string fileName)
        {
            await MigrateDataV100ToV110Async(folder, fileName);
            await MigrateDataV110ToV120Async(folder, fileName);
        }

        private static async Task MigrateDataV110ToV120Async(StorageFolder folder, string fileName)
        {
            try
            {
                var fileV110 = await ApplicationData.Current.RoamingFolder.GetFileAsync(GetArchiveFileName(fileName));  // Old file is stored in Roaming folder
                await fileV110.MoveAsync(folder);   // Move the file into Local folder
            }
            catch (FileNotFoundException)
            {
                return;
            }
        }

        private static async Task MigrateDataV100ToV110Async(StorageFolder folder, string fileName)
        {
            try
            {
                using (var copyStream = new MemoryStream())
                {
                    // When the migration was already done then fileName does not exists anymore in the folder.
                    using (var oldStream = await folder.OpenStreamForReadAsync(fileName))
                    {
                        await oldStream.CopyToAsync(copyStream);
                    }

                    copyStream.Seek(0, SeekOrigin.Begin);
                    using (var archiveStream = await folder.OpenStreamForWriteAsync(GetArchiveFileName(fileName), CreationCollisionOption.ReplaceExisting))
                    using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                        using (var newStream = entry.Open())
                        {
                            await copyStream.CopyToAsync(newStream);
                            await newStream.FlushAsync();
                        }
                    }
                }

                var file = await folder.GetFileAsync(fileName);
                await file.DeleteAsync();
            }
            catch (FileNotFoundException)
            {
                return;
            }
        }
    }
}
