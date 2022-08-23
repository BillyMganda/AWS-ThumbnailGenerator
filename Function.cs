using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Util;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Amazon.S3.Model;
using SixLabors.ImageSharp.Formats.Jpeg;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ThumbnailGenerator;

public class Function
{
    IAmazonS3 S3Client { get; set; }

    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public Function()
    {
        S3Client = new AmazonS3Client();
    }

    /// <summary>
    /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
    /// </summary>
    /// <param name="s3Client"></param>
    public Function(IAmazonS3 s3Client)
    {
        this.S3Client = s3Client;
    }
    
    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
    /// to respond to S3 notifications.
    /// </summary>
    /// <param name="evnt"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<string?> FunctionHandler(S3Event evnt, ILambdaContext context)
    {
        string[] fileExtentions = new string[] { ".jpg", ".jpeg" };
        var s3Event = evnt.Records?[0].S3;
        if (s3Event == null)
        {
            return null;
        }

        try
        {
            //Enumerates all files received as part of S3Event
            foreach (var record in evnt.Records)
            {
                LambdaLogger.Log("----> File: " + record.S3.Object.Key);
                if (!fileExtentions.Contains(Path.GetExtension(record.S3.Object.Key).ToLower()))
                {
                    LambdaLogger.Log("File Extention is not supported - " + s3Event.Object.Key);
                    continue;
                }

                string suffix = Path.GetExtension(record.S3.Object.Key).ToLower();
                Stream imageStream = new MemoryStream();
                //Downloads file from S3 bucket
                using (var objectResponse = await S3Client.GetObjectAsync(record.S3.Bucket.Name, record.S3.Object.Key))
                {
                    using (Stream responseStream = objectResponse.ResponseStream)
                    {
                        using (var image = Image.Load(responseStream))
                        {
                            // Resizes and converts it to black and white
                            image.Mutate(ctx => ctx.Grayscale().Resize(200, 200));
                            image.Save(imageStream, new JpegEncoder());
                            imageStream.Seek(0, SeekOrigin.Begin);
                        }
                    }
                }

                // Creating a new S3 ObjectKey for the thumbnails
                string thumbnailObjectKey = null;
                int endSlash = record.S3.Object.Key.ToLower().LastIndexOf("/");
                if (endSlash > 0)
                {
                    string S3ObjectName = record.S3.Object.Key.ToLower().Substring(endSlash + 1);
                    int beginSlash = 0;
                    if (endSlash > 0)
                    {
                        beginSlash = record.S3.Object.Key.ToLower().Substring(0, endSlash - 1).LastIndexOf("/");
                        if (beginSlash > 0)
                        {
                            thumbnailObjectKey =
                                record.S3.Object.Key.ToLower().Substring(0, beginSlash) +
                                "thumbnails/" +
                                S3ObjectName;
                        }
                        else
                        {
                            thumbnailObjectKey = "thumbnails/" + S3ObjectName;
                        }
                    }
                }
                else
                {
                    thumbnailObjectKey = "thumbnails/" + record.S3.Object.Key.ToLower();
                }

                LambdaLogger.Log("----> Thumbnail file Key: " + thumbnailObjectKey);

                //Saves generated thumbnail back to S3 bucket:
                await S3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = record.S3.Bucket.Name,
                    Key = thumbnailObjectKey,
                    InputStream = imageStream
                });
            }

            LambdaLogger.Log("Processed " + evnt.Records.Count.ToString());

            return null;
        }
        catch (Exception e)
        {
            context.Logger.LogLine($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}");
            context.Logger.LogLine($"Make sure they exist and your bucket is in the same region as this function");
            context.Logger.LogLine(e.Message);
            context.Logger.LogLine(e.StackTrace);
            throw;
        }
    }
}