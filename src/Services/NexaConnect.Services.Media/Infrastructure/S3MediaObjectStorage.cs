using Amazon.S3;using Amazon.S3.Model;using NexaConnect.Services.Media.Application;
namespace NexaConnect.Services.Media.Infrastructure;
public sealed class MediaStorageOptions{public string Bucket{get;set;}="";}
public sealed class S3MediaObjectStorage(IAmazonS3 s3,IConfiguration configuration):IMediaObjectStorage
{
 readonly string bucket=configuration["MediaStorage:Bucket"]??throw new InvalidOperationException("MediaStorage:Bucket is required.");
 public Task<string>CreateUploadUrlAsync(string key,string type,long size,string checksum,TimeSpan life,CancellationToken c){var request=new GetPreSignedUrlRequest{BucketName=bucket,Key=key,Verb=HttpVerb.PUT,ContentType=type,Expires=DateTime.UtcNow.Add(life)};request.Headers["x-amz-checksum-sha256"]=Convert.ToBase64String(Convert.FromHexString(checksum));return s3.GetPreSignedURLAsync(request);}
 public Task<string>CreateDownloadUrlAsync(string key,TimeSpan life,CancellationToken c)=>s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest{BucketName=bucket,Key=key,Verb=HttpVerb.GET,Expires=DateTime.UtcNow.Add(life)});
 public async Task<StoredObjectInfo?>InspectAsync(string key,CancellationToken c){try{var value=await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest{BucketName=bucket,Key=key,ChecksumMode=ChecksumMode.ENABLED},c);string? hash=string.IsNullOrWhiteSpace(value.ChecksumSHA256)?null:Convert.ToHexString(Convert.FromBase64String(value.ChecksumSHA256)).ToLowerInvariant();return new(value.ContentLength,hash);}catch(AmazonS3Exception e)when(e.StatusCode==System.Net.HttpStatusCode.NotFound){return null;}}
 public Task DeleteAsync(string key,CancellationToken c)=>s3.DeleteObjectAsync(new DeleteObjectRequest{BucketName=bucket,Key=key},c);
}
