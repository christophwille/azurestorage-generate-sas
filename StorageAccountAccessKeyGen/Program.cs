using Azure.Identity;
using Azure.Storage.Sas;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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

        static async Task Main()
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);

            IConfigurationRoot configuration = builder.Build();
            string storageConnectionString = configuration["storageConnectionString"];

            var kvPairs = GetKeyValuePairsFromStorageConnectionString(storageConnectionString);
            var accountName = kvPairs["AccountName"];
            var accountKey = kvPairs["AccountKey"];
            var blobServiceEndpoint = $"https://{accountName}.blob.core.windows.net";

            Console.WriteLine("Scenario #1: Simple file SAS URI with connection string");
            var blobClient = GetBlobContainerClient(storageConnectionString, DemoContainer);
            bool canGenerateSasUri = blobClient.CanGenerateSasUri;
            Uri sasUri = GenerateSasReadUriForFile(DemoFile, 5, blobClient);
            Console.WriteLine(sasUri + "\r\n");

            Console.WriteLine("Scenario #2: Storage account SAS URI for Storage Explorer to list/download blobs");
            Uri accountSasUri = GetAccountSas(accountName, accountKey, blobServiceEndpoint, 10);
            Console.WriteLine(accountSasUri + "\r\n");

            // Make sure to verify Azure Service Authentication / Account Selection in Tools / Options !
            Console.WriteLine("Scenario #3: Using Managed Identity / AAD credentials instead of connection strings");
            BlobServiceClient blobClientTokenCred = new BlobServiceClient(new Uri(blobServiceEndpoint), new VisualStudioCredential());
            bool canTokenCredClientGenerateSasUri = blobClient.CanGenerateSasUri;
            Uri sasUri_UserDelegationKey = await GenerateSasReadUriForFile_UserDelegationKey(blobClientTokenCred, DemoContainer, DemoFile, 5);
            Console.WriteLine(sasUri_UserDelegationKey + "\r\n");

            Console.WriteLine("Scenario #4: Get a connection string for multiple services in one go");
            string multiConnectionString = GetAccountSasMultiServiceConnectionString(accountName, accountKey, 10);
            Console.WriteLine(multiConnectionString + "\r\n");
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

        static string GetAccountSasMultiServiceConnectionString(string storageAccountName, string storageAccountKey, int expiresInHours)
        {
            AccountSasBuilder sas = new AccountSasBuilder
            {
                Services = AccountSasServices.Blobs | AccountSasServices.Tables,
                ResourceTypes = AccountSasResourceTypes.All,
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(expiresInHours),
                Protocol = SasProtocol.Https
            };

            sas.SetPermissions(AccountSasPermissions.Read | AccountSasPermissions.List);

            StorageSharedKeyCredential credential = new StorageSharedKeyCredential(storageAccountName, storageAccountKey);
            string sasQuery = sas.ToSasQueryParameters(credential).ToString();

            var blobServiceEndpoint = $"https://{storageAccountName}.blob.core.windows.net";
            var tableServiceEndpoint = $"https://{storageAccountName}.blob.table.windows.net";

            return $"BlobEndpoint={blobServiceEndpoint};TableEndpoint={tableServiceEndpoint};SharedAccessSignature={sasQuery}";
        }

        // Parts are from https://stackoverflow.com/a/59973193/141927 and other comments pieced together for my use case
        static async Task<Uri> GenerateSasReadUriForFile_UserDelegationKey(BlobServiceClient blobServiceClient, string containerName, string blobName, int expiresInHours)
        {
            // This is a rather expensive call, best to create one user delegation key for a longer period and cache it
            UserDelegationKey userDelegationKey = await blobServiceClient.GetUserDelegationKeyAsync
            (
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(5d)
            ).ConfigureAwait(false);

            BlobSasBuilder sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow,
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(expiresInHours)
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            string sasToken = sasBuilder.ToSasQueryParameters(userDelegationKey, blobServiceClient.AccountName).ToString();

            UriBuilder fullUri = new UriBuilder()
            {
                Scheme = "https",
                Host = string.Format("{0}.blob.core.windows.net", blobServiceClient.AccountName),
                Path = string.Format("{0}/{1}", containerName, blobName),
                Query = sasToken
            };

            return fullUri.Uri;

            //// Another way to go
            //BlobUriBuilder blobUriBuilder = new(blobServiceClient.Uri)
            //{
            //    Sas = sasBuilder.ToSasQueryParameters(userDelegationKey, blobServiceClient.AccountName)
            //};

            //var uri = blobUriBuilder.ToUri();
        }
    }
}