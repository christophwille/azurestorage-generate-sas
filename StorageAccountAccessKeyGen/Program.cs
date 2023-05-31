using Azure.Storage.Sas;
using Azure.Storage;
using Azure.Storage.Blobs;

using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace StorageAccountAccessKeyGen
{
    internal class Program
    {
        static readonly string DemoContainer = "demo";
        static readonly string DemoFile = "myfile.txt";

        static async Task Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);

            IConfigurationRoot configuration = builder.Build();
            string storageConnectionString = configuration["storageConnectionString"];


            var blobClient = GetBlobContainerClient(storageConnectionString, DemoContainer);
            bool canGenerateSasUri = blobClient.CanGenerateSasUri;
            Uri sasUri = GenerateSasReadUriForFile(DemoFile, 5, blobClient);

            // TODO
        }

        static BlobContainerClient GetBlobContainerClient(string storageConnectionString, string containerName)
        {
            var blobServiceClient = new BlobServiceClient(storageConnectionString);
            return blobServiceClient.GetBlobContainerClient(containerName);
        }

        static Uri GenerateSasReadUriForFile(string fileName, int expireHours, BlobContainerClient blobContainerClient)
        {
            BlobSasBuilder sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = blobContainerClient.Name,
                BlobName = fileName,
                Resource = "b",
                StartsOn = DateTime.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTime.UtcNow.AddHours(expireHours)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var blobClient = blobContainerClient.GetBlobClient(fileName);
            return blobClient.GenerateSasUri(sasBuilder);
        }
    }
}