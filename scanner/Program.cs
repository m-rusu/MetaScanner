using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;

class Program 
{
    static async Task Main(string[] args) 
    {

        if (args.Length != 2) 
        {
            Console.WriteLine("This program needs 2 arguments: <file_path> <api_key>");
            return;
        }

        string filePath = args[0];
        string apiKey = args[1];

        string sha256Hash = CalculateSHA256(filePath);

        bool cachedResultsFound = await PerformHashLookup(sha256Hash, apiKey);

        if (!cachedResultsFound)
        {
            Console.WriteLine("Cached results not found! Wait for results.");

            string dataId = await UploadFileAndGetID(filePath, apiKey);

            string result = await PollAPIForResults(dataId, apiKey);

            DisplayResults(result);
        }
        else
        {
            Console.WriteLine("Cached results found!");

            string cachedResult = await RetrieveCachedResults(sha256Hash, apiKey);

            DisplayResults(cachedResult);
        }

    }

    static string CalculateSHA256(string filePath) {

        using(SHA256 mySHA256 = SHA256.Create()) 
        {
            using (FileStream stream = File.OpenRead(filePath)) 
            {
                try 
                {
                    stream.Position = 0;
                    return ByteArrayToString(mySHA256.ComputeHash(stream));
                }
                catch (IOException e) 
                {
                    Console.WriteLine($"I/O Exception: {e.Message}");
                }
                catch (UnauthorizedAccessException e) 
                {
                    Console.WriteLine($"Access Exception: {e.Message}");
                }
            }
        }
        return "";
    }

    public static string ByteArrayToString(byte[] array)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < array.Length; i++)
        {
            sb.Append(array[i].ToString("x2"));
        }
        return sb.ToString();
    }

    static async Task<bool> PerformHashLookup(string sha256Hash, string apiKey)
    {
        try
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"https://api.metadefender.com/v4/hash/{sha256Hash}"),
                };
                request.Headers.Add("apikey", apiKey);

                using (var response = await client.SendAsync(request))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error performing hash lookup: {e.Message}");
            return false;
        }
    }

    static async Task<string> UploadFileAndGetID(string filePath, string apiKey)
    {
        try
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://api.metadefender.com/v4/file"),
                };
                request.Headers.Add("apikey", apiKey);

                ByteArrayContent fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Content = new MultipartFormDataContent
                {
                    { fileContent, "file", Path.GetFileName(filePath) }
                };
            
                using (var response = await client.SendAsync(request))
                {
                    if(response.IsSuccessStatusCode)
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();

                        JsonDocument jsonDoc = JsonDocument.Parse(responseContent);
                        string dataId = jsonDoc.RootElement.GetProperty("data_id").GetString();
                        return dataId;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to upload file. Status code: {response.StatusCode}");
                    }
                }
            }

        }
        catch (Exception e)
        {
            Console.WriteLine($"Error uploading file: {e.Message}");
        }

        return "";
    }

    static async Task<string> PollAPIForResults(string dataId, string apiKey)
    {
        try
        {
            using (var client = new HttpClient())
            {
                while (true)
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri($"https://api.metadefender.com/v4/file/{dataId}")
                    };
                    request.Headers.Add("apikey", apiKey);

                    using (var response = await client.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string responseContent = await response.Content.ReadAsStringAsync();

                            JsonDocument jsonDoc = JsonDocument.Parse(responseContent);

                            int progressPercentage = jsonDoc.RootElement.GetProperty("scan_results").GetProperty("progress_percentage").GetInt32();
                            if (progressPercentage == 100)
                            {
                                return responseContent;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Failed to retrieve scan result. Status code: {response.StatusCode}");
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error polling API for scan result: {e.Message}");
        }
        return "";
    }

    static async Task<string> RetrieveCachedResults(string sha256Hash, string apiKey)
    {
        try
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"https://api.metadefender.com/v4/hash/{sha256Hash}")
                };
                request.Headers.Add("apikey", apiKey);

                using (var response = await client.SendAsync(request))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();
                        return responseContent;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to retrieve cached results. Status code: {response.StatusCode}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error retrieving cached results: {e.Message}");
        }
        return "";
    }

    static void DisplayResults(string result)
    {
        if (string.IsNullOrEmpty(result))
        {
            Console.WriteLine("No results to display.");
            return;
        }

        try
        {
            Console.WriteLine();
            JsonDocument jsonDoc = JsonDocument.Parse(result);
            var fileInfo = jsonDoc.RootElement.GetProperty("file_info");

            string fileName = fileInfo.GetProperty("display_name").GetString();
            Console.WriteLine($"Filename: {fileName}");

            string overallStatus = jsonDoc.RootElement.GetProperty("scan_results").GetProperty("scan_all_result_a").GetString();
            Console.WriteLine($"OverallStatus: {overallStatus}");

            var scanResults = jsonDoc.RootElement.GetProperty("scan_results").GetProperty("scan_details");
            foreach (var engine in scanResults.EnumerateObject())
            {
                string engineName = engine.Name;
                var engineInfo = engine.Value;
                string threatFound = engineInfo.GetProperty("threat_found").GetString();
                int scanResult = engineInfo.GetProperty("scan_result_i").GetInt32();
                string defTime = engineInfo.GetProperty("def_time").GetString();

                Console.WriteLine($"Engine: {engineName}");
                Console.WriteLine($"ThreatFound: {threatFound}");
                Console.WriteLine($"ScanResult: {scanResult}");
                Console.WriteLine($"DefTime: {defTime}");
                Console.WriteLine();
            }
            Console.WriteLine("END");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error displaying results: {e.Message}");
        }

    }
}
