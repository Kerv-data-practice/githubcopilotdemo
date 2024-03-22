
#define DEBUG
using System;
using System.Configuration;
using System.Linq;
using System.Web.Http;
using System.Web.Mvc;
using Autofac;
using Autofac.Integration.WebApi;
using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Security.KeyVault.Secrets;
using GetD365DataAPI.Controllers;
using GetD365DataAPI.models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using ConfigurationBuilder = Microsoft.Extensions.Configuration.ConfigurationBuilder;

namespace GetD365DataAPI
{
    public class WebApiApplication : System.Web.HttpApplication, IDisposable
    {
        public static IConfiguration Configuration;
        private static IConfigurationRefresher _configurationRefresher;
        private IContainer _container;

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);

            // Initialize configuration and services
            SetupConfiguration();
            RegisterServices();
            // Create an instance of the BackgroundService and start it
            var backgroundService = new BackgroundService(Configuration);
            backgroundService.Start();
        }

        private void SetupConfiguration()
        {
            Configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(System.Configuration.ConfigurationManager.AppSettings["AzureAppConfigConnectionString"])
                        .Select("WebAPI:Entities:*")
                        .ConfigureRefresh(refresh =>
                        {
                            refresh.Register("WebAPI:Settings:Sentinel", refreshAll: true)
                                .SetCacheExpiration(new TimeSpan(0, 0, 10));
                        });
                    _configurationRefresher = options.GetRefresher();
                })
                .Build();

            // Create a SecretClient using the specified managed identity
            var keyVaultUri = new Uri(System.Configuration.ConfigurationManager.AppSettings["KeyVaultUri"]);
            var secretClient = new SecretClient(keyVaultUri, new DefaultAzureCredential());

            // Retrieve all secrets from Key Vault
            var allSecrets = secretClient.GetPropertiesOfSecrets()
                .Select(secretItem => secretClient.GetSecret(secretItem.Name).Value)
                .ToList();

            // Load all secrets into configuration
            foreach (var secret in allSecrets)
            {
                Configuration["KV:" + secret.Name] = secret.Value;
            }


            //initilise service bus 

            //            var webApiKeys = Configuration.AsEnumerable()
            //                .Where(kvp => kvp.Key.StartsWith("WebAPI:Entities:"))
            //                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            //            foreach (var kvp in webApiKeys)
            //            {
            //                string keyIdentifier = kvp.Key.Split(':')[2];
            //                string value = kvp.Value;
            //                ConfigEntity entityConfig = JsonConvert.DeserializeObject<ConfigEntity>(value);
            //#if !DEBUG
            //                SetupServiceBusTopic(entityConfig, keyIdentifier);
            //#endif
            //            }
            //TODO:Fix service bus initilisation
#if !DEBUG
                SetupServiceBusTopic(entityConfig, keyIdentifier);
#endif

        }
        private void SetupServiceBusTopic(ConfigEntity entityConfig, string keyIdentifier)
        {
            string serviceBusNamespace = System.Configuration.ConfigurationManager.AppSettings["AzureServiceBusNamespace"];
            string fullyQualifiedNamespace = $"{serviceBusNamespace}.servicebus.windows.net";
            
            // Create a ServiceBusAdministrationClient using DefaultAzureCredential
            ServiceBusAdministrationClient managementClient = new ServiceBusAdministrationClient(fullyQualifiedNamespace, new DefaultAzureCredential());

            string topicName = entityConfig.EntityName;

            // Check if the topic with the same name exists, if not, create it
            if (!managementClient.TopicExistsAsync(topicName).Result)
            {
                managementClient.CreateTopicAsync(topicName).Wait();
            }

            // Create subscriptions based on the connector
            foreach (var output in entityConfig.output)
            {
                string subscriptionName = output.connector;

                // Check if the subscription with the same name exists, if not, create it
                if (!managementClient.SubscriptionExistsAsync(topicName, subscriptionName).Result)
                {
                   // managementClient.CreateSubscriptionAsync(topicName, subscriptionName).Wait();
                    managementClient.CreateSubscriptionAsync(
                        new CreateSubscriptionOptions(topicName, subscriptionName),
                        new CreateRuleOptions("DefaultFilter", new SqlRuleFilter($"user.KI='{keyIdentifier}'"))).Wait();

                }
                
                
            }
        }




        private void RegisterServices()
        {
            var builder = new ContainerBuilder();

            // Initialize the ConnectionHelper with the configuration
            ConnectionHelper.Initialize(Configuration);

            // Register your services and dependencies here
            builder.Register(c => ConnectionHelper.CreateOrganizationService()).As<IOrganizationService>();

            // Register IConfigurationRefresher as an instance
            builder.RegisterInstance(_configurationRefresher).As<IConfigurationRefresher>();

            // Register IConfiguration as an instance (assuming Configuration is your instance of IConfiguration)
            builder.RegisterInstance(Configuration).As<IConfiguration>();

            // Register the Web API controllers
            builder.RegisterApiControllers(typeof(WebApiApplication).Assembly);

            // Build the Autofac container
            _container = builder.Build();

            // Set the Web API dependency resolver to use Autofac
            GlobalConfiguration.Configuration.DependencyResolver = new AutofacWebApiDependencyResolver(_container);
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            try
            {
                // Refresh configuration asynchronously
                _ = _configurationRefresher.TryRefreshAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Handle or log the exception appropriately
                // Consider using a dedicated error handling mechanism
                
            }
        }

        public void Dispose()
        {
            // Dispose of Configuration and ConfigurationRefresher if needed
            // For example, ConfigurationRefresher.Dispose() if it implements IDisposable
        }
    }
}
