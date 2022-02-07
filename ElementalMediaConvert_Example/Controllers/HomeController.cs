using Amazon;
using Amazon.MediaConvert;
using Amazon.MediaConvert.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ElementalMediaConvert_Example.Controllers
{
    public class HomeController : Controller
    {
        private readonly string _mediaConvertRole;
        private readonly AmazonMediaConvertClient _client;
        private readonly RegionEndpoint _regionEndpoint;

        public HomeController()
        {
            _mediaConvertRole = "arn da role";
            var region = "sa-east-1";

            _regionEndpoint = RegionEndpoint.GetBySystemName(region);
            var mediaConvertEndpoint = ListarEndpoints(_regionEndpoint).Result;

            var config = new AmazonMediaConvertConfig { ServiceURL = mediaConvertEndpoint };
            _client = new AmazonMediaConvertClient(config);
        }

        public IActionResult Index()
        {
            var jobs = ListarJobs(_client).Result;

            ViewBag.Jobs = jobs;
            return View();
        }

        [HttpPost]
        [RequestFormLimits(MultipartBodyLengthLimit = 209715200)]
        [RequestSizeLimit(209715200)]
        public async Task<IActionResult> IncluirMidia(IFormFile file)
        {
            try
            {
                if (file == null)
                {
                    return View();
                }

                var fileName = Path.GetFileName(file.FileName);
                using (var stream = file.OpenReadStream())
                {
                    var s3Client = new AmazonS3Client(RegionEndpoint.GetBySystemName("sa-east-1"));


                    var fileTransferUtility = new TransferUtility(s3Client);
                    var fileTransferUtilityRequest = new TransferUtilityUploadRequest
                    {
                        BucketName = "nome do bucket",
                        InputStream = stream,
                        Key = $"nome da pasta/{fileName}"
                    };

                    await fileTransferUtility.UploadAsync(fileTransferUtilityRequest);
                }

                var createJobStatus = await CreateJob(_client, _mediaConvertRole, $"caminho do bucket até a pasta/{file.FileName}", "nome do bucket");

                return RedirectToPage("Uploads");
            }
            catch (Exception ex)
            {
                return RedirectToPage("Uploads");
            }
        }

        public async Task<string> ListarEndpoints(RegionEndpoint region)
        {
            var config = new AmazonMediaConvertConfig { RegionEndpoint = region };
            var client = new AmazonMediaConvertClient(config);
            var request = new DescribeEndpointsRequest();
            var response = await client.DescribeEndpointsAsync(request);

            return response.Endpoints.FirstOrDefault()?.Url;
        }

        public async Task<JobStatus> CreateJob(AmazonMediaConvertClient client, string mediaConvertRole, string inputFile, string s3Bucket)
        {
            var request = new CreateJobRequest();
            request.Role = mediaConvertRole;
            request.UserMetadata.Add("Customer", "Amazon");

            var jobSettings = new JobSettings
            {
                AdAvailOffset = 0,
                TimecodeConfig = new TimecodeConfig() { Source = TimecodeSource.EMBEDDED }
            };

            request.Settings = jobSettings;
            request.Role = mediaConvertRole;

            var input = new Input()
            {
                FileInput = $"s3://{inputFile}",
                AudioSelectors = new Dictionary<string, AudioSelector>
                {
                    {
                        "Audio Selector 1", new AudioSelector
                        {
                            Offset = 0,
                            DefaultSelection = AudioDefaultSelection.DEFAULT,
                            ProgramSelection = 1
                        }
                    }
                },
                VideoSelector = new VideoSelector { ColorSpace = ColorSpace.FOLLOW },
                FilterEnable = InputFilterEnable.AUTO,
                PsiControl = InputPsiControl.USE_PSI,
                DeblockFilter = InputDeblockFilter.DISABLED,
                DenoiseFilter = InputDenoiseFilter.DISABLED,
                TimecodeSource = InputTimecodeSource.EMBEDDED,
                FilterStrength = 0
            };

            request.Settings.Inputs.Add(input);

            var outputGroup = new OutputGroup
            {
                Name = "File Group",
                OutputGroupSettings = new OutputGroupSettings
                {
                    Type = OutputGroupType.FILE_GROUP_SETTINGS,
                    FileGroupSettings = new FileGroupSettings { Destination = $"s3://{inputFile}" },
                },

                Outputs = new List<Output>()
                {
                   new Output()
                   {
                       Preset = "Test Preset",
                       NameModifier = "_cli",
                   }
                }
            };

            request.Settings.OutputGroups.Add(outputGroup);

            try
            {
                var response = await client.CreateJobAsync(request);
                return response.Job.Status;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public IActionResult ExecutarMidia(string id, int index)
        {
            var job = GetJob(_client, id).Result;
            var presignedUrl = GetPresignedUrl(job.Settings.Inputs[0].FileInput.Replace("s3://nome do bucket/", ""));

            return Redirect(presignedUrl);
        }

        private string GetPresignedUrl(string outputKey)
        {
            var s3Client = new AmazonS3Client(_regionEndpoint);
            var request = new GetPreSignedUrlRequest()
            {
                BucketName = "nome do bucket",
                Key = outputKey,
                Expires = DateTime.UtcNow.AddMinutes(10)
            };
            var presignedUrl = s3Client.GetPreSignedURL(request);
            return presignedUrl;
        }

        private async Task<Job> GetJob(AmazonMediaConvertClient client, string jobId)
        {
            var request = new GetJobRequest() { Id = jobId };
            var response = await client.GetJobAsync(request);
            return response.Job;
        }

        public async Task<List<Job>> ListarJobs(AmazonMediaConvertClient client)
        {
            try
            {
                var jobs = new List<Job>();
                var request = new ListJobsRequest();
                var response = await client.ListJobsAsync(request);

                response.Jobs = response.Jobs.Where(x => x.Status == "COMPLETE").ToList();

                foreach (var item in response.Jobs)
                {
                    item.Settings.Inputs[0].FileInput = item.Settings.Inputs[0].FileInput.Split("nome da pasta/").LastOrDefault();
                    jobs.Add(item);
                }

                AmazonS3Client s3Client = new AmazonS3Client();
                string bucketName = "nome do bucket";
                string prefix = "nome da pasta/";

                ListObjectsRequest requestS3 = new ListObjectsRequest
                {
                    BucketName = bucketName,
                    Prefix = prefix,
                };

                ListObjectsResponse responseS3 = await s3Client.ListObjectsAsync(requestS3);

                foreach (S3Object itemsInsideDirectory in responseS3.S3Objects.Where(x => !x.Key.Trim().Contains(".mp4")))
                {
                    jobs.Add(new Job
                    {
                        CreatedAt = itemsInsideDirectory.LastModified,
                        Settings = new JobSettings
                        {
                            Inputs = new List<Input>
                            {
                                new Input
                                {
                                    FileInput = itemsInsideDirectory.Key.Split("nome da pasta/").LastOrDefault()
                                }
                            }
                        },
                        Status = itemsInsideDirectory.StorageClass.Value
                    });
                }

                return jobs;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<ActionResult> DownloadMidia(string arquivo)
        {
            AmazonS3Client s3Client = new AmazonS3Client();

            var objeto = new GetObjectRequest
            {
                BucketName = "nome do bucket",
                Key = $"nome da pasta/{arquivo}",
            };

            var responseS3 = await s3Client.GetObjectAsync(objeto);

            using (Stream responseStream = responseS3.ResponseStream)
            {
                return new FileContentResult(ReadStream(responseStream), "application/octet-stream")
                {
                    FileDownloadName = arquivo,
                };
            }
        }

        private static byte[] ReadStream(Stream responseStream)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }

        public async Task<IActionResult> ExcluirMidia(string arquivo, string id)
        {
            try
            {
                if (arquivo.ToLower().Contains(".mp4"))
                {
                    var response = await _client.CancelJobAsync(new CancelJobRequest { Id = id });
                }

                AmazonS3Client s3Client = new AmazonS3Client();

                var deleteObjectRequest = new DeleteObjectRequest
                {
                    BucketName = "nome do bucket",
                    Key = $"nome da pasta/{arquivo}"
                };

                await s3Client.DeleteObjectAsync(deleteObjectRequest);
                return RedirectToAction("Index");

            }
            catch (Exception ex)
            {
                return RedirectToAction("Index");
            }
        }
    }
}