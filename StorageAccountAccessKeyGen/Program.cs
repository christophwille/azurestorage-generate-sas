using Azure.Storage.Sas;
using Azure.Storage;
using Azure.Storage.Blobs;

using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

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

            // Simple file SAS URI
            var blobClient = GetBlobContainerClient(storageConnectionString, DemoContainer);
            bool canGenerateSasUri = blobClient.CanGenerateSasUri;
            Uri sasUri = GenerateSasReadUriForFile(DemoFile, 5, blobClient);

            // Storage account SAS URI
            var kvPairs = GetKeyValuePairsFromStorageConnectionString(storageConnectionString);
            var accountName = kvPairs["AccountName"];
            var accountKey = kvPairs["AccountKey"];
            var blobServiceEndpoint = $"https://{accountName}.blob.core.windows.net";
            Uri accountSasUri = GetAccountSas(accountName, accountKey, blobServiceEndpoint, 10);

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

        // https://www.craftedforeveryone.com/beginners-guide-and-reference-to-azure-blob-storage-sdk-v12-dot-net-csharp/#get_account_name_and_account_key_from_a_connection_string
        static Dictionary<string, string> GetKeyValuePairsFromStorageConnectionString(string storageConnectionString)
        {
            var settings = new Dictionary<string, string>();

            foreach (var nameValue in storageConnectionString.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var splittedNameValue = nameValue.Split(new char[] { '=' }, 2);
                settings.Add(splittedNameValue[0], splittedNameValue[1]);
            }

            return settings;
        }

        // https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/storage/Azure.Storage.Blobs/samples/Sample02_Auth.cs
        static Uri GetAccountSas(string storageAccountName, string storageAccountKey, string blobServiceEndpoint, int expiresInHours)
        {
            AccountSasBuilder sas = new AccountSasBuilder
            {
                Services = AccountSasServices.Blobs,

                // F12 on AccountSasResourceTypes - to be able to download files from containers in a Storage account we need all of those
                ResourceTypes = AccountSasResourceTypes.All,
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(expiresInHours),
                Protocol = SasProtocol.Https
            };

            // Allow read access (list is needed because otherwise Storage Explorer won't connect at all)
            sas.SetPermissions(AccountSasPermissions.Read | AccountSasPermissions.List);

            StorageSharedKeyCredential credential = new StorageSharedKeyCredential(storageAccountName, storageAccountKey);
            UriBuilder sasUri = new UriBuilder($"{blobServiceEndpoint}");
            sasUri.Query = sas.ToSasQueryParameters(credential).ToString();

            return sasUri.Uri;
        }
    }
}