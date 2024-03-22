using Microsoft.Extensions.Configuration;
using System;
using System.Configuration;
using System.Net.Http;
using System.Timers;
using GetD365DataAPI.models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;


namespace GetD365DataAPI
{
    public class BackgroundService
    {
        private readonly IConfiguration _configuration;
       // private  HttpClient _httpClient;
        private readonly Timer _timer;
        //private readonly ILogger _logger;

        public BackgroundService(IConfiguration configuration )
        {
            _configuration = configuration;
            _timer = new Timer(30000); // Set the timer interval to 1 minute (adjust as needed)
            _timer.Elapsed += (sender, e) => ExecuteScheduledCalls();
        }

        public void Start()
        {
            _timer.Start();
        }

        private void ExecuteScheduledCalls()
        {
            foreach (var key in _configuration.AsEnumerable())
            {
                if (key.Key.StartsWith("WebAPI:Entities:"))
                {
                    string routeName =  "/api/" + key.Key.Split(':')[2];
                    var jsonObject = JsonConvert.DeserializeObject<ConfigEntity>(key.Value);
                    string frequency = jsonObject.RefreshInterval;

                    string[] frequencyparts = frequency.Split(',');
                    
                    // Parse the frequency setting to calculate milliseconds
                    if (frequencyparts.Length == 3)
                    {
                        TimeSpan interval = new TimeSpan(int.Parse(frequencyparts[0]), int.Parse(frequencyparts[1]),
                            int.Parse(frequencyparts[2]));
                        var timer = new Timer(interval.TotalMilliseconds);
                        //TODO: review if this needs to be run at first run
                        timer.Elapsed += (sender, e) => ExecuteApiCall(routeName);
                        timer.Start();

                        Console.WriteLine(
                            $"Scheduled API call for route: {routeName}, endpoint: {routeName}, frequency: {frequency}");
                    }
                    else
                    {
                        Console.WriteLine($"Invalid frequency format for route: {routeName}");
                    }
                }
            }
        }

        private void ExecuteApiCall(string endpoint)
        {
            try
            {
                var baseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["BaseURL"]);
                using (var client = new HttpClient() { BaseAddress = baseAddress })

                {
                    // Make an HTTP GET request using the relative URI (endpoint)
                    //TODO: Review the BASE URL Settings

                    HttpResponseMessage response = client.GetAsync(  endpoint).Result;
                    //TODO: avoid duplicate calls to the same API
                    if (response.IsSuccessStatusCode)
                    {
                        // API call was successful
                        Console.WriteLine($"API call to {endpoint} succeeded at {DateTime.Now}");
                    }
                    else
                    {
                        // API call failed
                        Console.WriteLine($"API call to {endpoint} failed. Status Code: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions that may occur during the API call
                Console.WriteLine($"API call to {endpoint} failed with an exception: {ex.Message}");
            }
        }
    }

}