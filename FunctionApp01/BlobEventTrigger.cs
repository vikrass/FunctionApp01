// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventGrid;
using System;
using Azure.Storage.Blobs;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Gif;
using Azure.Messaging.EventGrid.SystemEvents;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Processing;
using Azure.Storage.Blobs.Specialized;

namespace FunctionApp01
{
    public static class BlobEventTrigger
    {
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureBlobStorage");

        private static string GetBlobNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        [FunctionName("BlobEventTrigger")]
        public static async Task RunAsync(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            ILogger log)
        {
            log.LogInformation(eventGridEvent.Data.ToString());
            try
            {
                if (eventGridEvent != null)
                {
                    var createdEvent = eventGridEvent.Data.ToObjectFromJson<StorageBlobCreatedEventData>();

                    if (createdEvent.Api is not "CreateBlob")
                    {
                        return;
                    }
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        var thumbnailWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH"));
                        var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAME");
                        var imagesContainerName = Environment.GetEnvironmentVariable("IMAGES_CONTAINER_NAME");

                        var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);

                        var blobImageContainerClient = blobServiceClient.GetBlobContainerClient(imagesContainerName);
                        var blobThumbnailContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);


                        var blobName = GetBlobNameFromUrl(createdEvent.Url);
                        BlockBlobClient blob = blobImageContainerClient.GetBlockBlobClient(blobName);

                        using (var blobStream = await blob.OpenReadAsync(false))
                        using (var output = new MemoryStream())
                        using (Image<Rgb24> image = (Image<Rgb24>)Image.Load(blobStream))
                        {
                            var divisor = image.Width / thumbnailWidth;
                            var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                            image.Mutate(x => x.Resize(thumbnailWidth, height));
                            image.Save(output, encoder);
                            output.Position = 0;
                            await blobThumbnailContainerClient.UploadBlobAsync(blobName, output);
                        }
                    }
                    else
                    {
                        log.LogInformation($"No encoder support for: {createdEvent.Url}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }
    }
}
