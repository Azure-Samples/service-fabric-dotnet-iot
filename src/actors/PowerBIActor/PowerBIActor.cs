// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PowerBIActor
{
    using System;
    using System.Fabric.Description;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;
    using IoTActor.Common;
    using Microsoft.IdentityModel.Clients.ActiveDirectory;
    using Microsoft.ServiceFabric.Actors;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    [ActorGarbageCollection(IdleTimeoutInSeconds = 60, ScanIntervalInSeconds = 10)]
    public class PowerBIActor : StatefulActor<PowerBIActorState>, IIoTActor
    {
        private static int maxEntriesPerRound = 100;
        private IActorTimer dequeueTimer = null;
        private bool dataSetCreated = false;
        private string dataSetID = string.Empty;
        private string addRowsUrl = "https://api.powerbi.com/v1.0/myorg/datasets/{0}/tables/{1}/rows";
        // settings
        private string clientId = string.Empty;
        private string username = string.Empty;
        private string password = string.Empty;
        private string powerBiResource = string.Empty;
        private string authority = string.Empty;
        private string powerBIBaseUrl = string.Empty;
        private string datasetName = string.Empty;
        private string tableName = string.Empty;
        private string dataSetSchema = string.Empty;

        public Task Post(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body)
        {
            IoTActorWorkItem workItem = new IoTActorWorkItem();
            workItem.DeviceId = DeviceId;
            workItem.EventHubName = EventHubName;
            workItem.ServiceBusNS = ServiceBusNS;
            workItem.Body = Body;

            this.State.Queue.Enqueue(workItem);

            return Task.FromResult(true);
        }

        protected override async Task OnActivateAsync()
        {
            if (this.State == null)
            {
                this.State = new PowerBIActorState();
            }

            await this.SetConfig();
            this.ActorService.ServiceInitializationParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent += this.ConfigChanged;
            ActorEventSource.Current.ActorMessage(this, "New Actor On Activate");

            // register a call back timer, that perfoms the actual send to PowerBI
            // has to iterate in less than IdleTimeout 
            this.dequeueTimer = this.RegisterTimer(
                this.SendToPowerBIAsync,
                false,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(10));

            await base.OnActivateAsync();
        }

        protected override async Task OnDeactivateAsync()
        {
            this.UnregisterTimer(this.dequeueTimer); // remove the actor timer
            await this.SendToPowerBIAsync(true); // make sure that no remaining pending records 
            await base.OnDeactivateAsync();
        }

        #region Config Management 

        private void ConfigChanged(object sender, System.Fabric.PackageModifiedEventArgs<System.Fabric.ConfigurationPackage> e)
        {
            this.SetConfig().Wait();
        }

        private async Task SetConfig()
        {
            ConfigurationSettings settingsFile =
                this.ActorService.ServiceInitializationParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings;

            ConfigurationSection configSection = settingsFile.Sections["PowerBI"];

            this.clientId = configSection.Parameters["ClientId"].Value;
            this.username = configSection.Parameters["Username"].Value;
            this.password = configSection.Parameters["Password"].Value;
            this.powerBiResource = configSection.Parameters["PowerBIResourceId"].Value;
            this.authority = configSection.Parameters["Authority"].Value;
            this.powerBIBaseUrl = configSection.Parameters["PowerBIBaseUrl"].Value;
            this.datasetName = configSection.Parameters["DatesetName"].Value;
            this.tableName = configSection.Parameters["TableName"].Value;


            ActorEventSource.Current.ActorMessage(
                this,
                "Config loaded \n Client:{0} \n Username:{1} \n Password:{2} \n Authority{3} \n PowerBiResource:{4} \n BaseUrl:{5} \n DataSet:{6} \n Table:{7}",
                this.clientId,
                this.username,
                this.password,
                this.authority,
                this.powerBiResource,
                this.powerBIBaseUrl,
                this.datasetName,
                this.tableName);


            using (
                StreamReader sr =
                    new StreamReader(
                        this.ActorService.ServiceInitializationParameters.CodePackageActivationContext.GetDataPackageObject("Data").Path +
                        @"\Datasetschema.json"))
            {
                this.dataSetSchema = await sr.ReadToEndAsync();
            }
        }

        #endregion

        #region Power BI Sending Logic

        private async Task<string> GetAuthTokenAsync()
        {
            AuthenticationContext authContext = new AuthenticationContext(this.authority);
            UserCredential userCredential = new UserCredential(this.username, this.password);
            AuthenticationResult result = await authContext.AcquireTokenAsync(this.powerBiResource, this.clientId, userCredential);
            return result.AccessToken;
        }

        private async Task EnsureDataSetCreatedAsync()
        {
            if (this.dataSetCreated)
            {
                return;
            }

            HttpRequestMessage ReqMessage;
            HttpResponseMessage ResponseMessage;
            HttpClient httpClient = new HttpClient();

            string AuthToken = await this.GetAuthTokenAsync();

            // get current datasets. 

            ReqMessage = new HttpRequestMessage();
            ReqMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);
            ReqMessage.Method = HttpMethod.Get;
            ReqMessage.RequestUri = new Uri(this.powerBIBaseUrl);
            ResponseMessage = await httpClient.SendAsync(ReqMessage);
            ResponseMessage.EnsureSuccessStatusCode();


            JObject j = JObject.Parse(await ResponseMessage.Content.ReadAsStringAsync());
            JArray arrDs = j["value"] as JArray;

            foreach (JToken entry in arrDs)
            {
                if (null != entry["id"] && this.datasetName == entry["name"].Value<string>())
                {
                    this.dataSetID = entry["id"].Value<string>();
                    this.dataSetCreated = true;
                    return;
                }
            }


            try
            {
                // not there create it 
                ReqMessage = new HttpRequestMessage();
                ReqMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);
                ReqMessage.Method = HttpMethod.Post;
                ReqMessage.RequestUri = new Uri(this.powerBIBaseUrl);
                ReqMessage.Content = new StringContent(this.dataSetSchema, Encoding.UTF8, "application/json");
                ResponseMessage = await httpClient.SendAsync(ReqMessage);
                ResponseMessage.EnsureSuccessStatusCode();

                j = JObject.Parse(await ResponseMessage.Content.ReadAsStringAsync());
                this.dataSetID = j["id"].Value<string>();


                ActorEventSource.Current.ActorMessage(this, "Dataset created");
            }
            catch (AggregateException aex)
            {
                AggregateException ae = aex.Flatten();

                foreach (Exception e in ae.InnerExceptions)
                {
                    ActorEventSource.Current.ActorMessage(this, "Error creating dataset E{0} , E:{1}", e.Message, e.StackTrace);
                }

                ActorEventSource.Current.ActorMessage(this, "Error will be ignored and actor will attempt to push the rows");
            }


            this.dataSetCreated = true;
        }


        private async Task SendToPowerBIAsync(object IsFinal)
        {
            if (0 == this.State.Queue.Count)
            {
                return;
            }

            await this.EnsureDataSetCreatedAsync();

            bool bFinal = (bool)IsFinal; // as in actor instance is about to get deactivated. 
            Task<string> tAuthToken = this.GetAuthTokenAsync();
            int nCurrent = 0;
            JArray list = new JArray();

            while ((nCurrent <= maxEntriesPerRound || bFinal) && (0 != this.State.Queue.Count))
            {
                list.Add(this.State.Queue.Dequeue().toJObject());
                nCurrent++;
            }


            try
            {
                var all = new { rows = list };
                string sContent = JsonConvert.SerializeObject(all);

                this.addRowsUrl = string.Format(this.addRowsUrl, this.dataSetID, this.tableName);


                string AuthToken = await tAuthToken;
                HttpClient client = new HttpClient();

                HttpRequestMessage requestMessage = new HttpRequestMessage();
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);
                requestMessage.Method = HttpMethod.Post;
                requestMessage.RequestUri = new Uri(this.addRowsUrl);
                requestMessage.Content = new StringContent(sContent, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.SendAsync(requestMessage);

                response.EnsureSuccessStatusCode();

                ActorEventSource.Current.ActorMessage(this, "Pushed to PowerBI:{0} Remaining {1}", sContent, this.State.Queue.Count);
            }
            catch (AggregateException ae)
            {
                ActorEventSource.Current.ActorMessage(this, "Power BI Actor encontered the followong error sending and will retry ");
                foreach (Exception e in ae.Flatten().InnerExceptions)
                {
                    ActorEventSource.Current.ActorMessage(this, "E:{0} StackTrack:{1}", e.Message, e.StackTrace);
                }
                ActorEventSource.Current.ActorMessage(this, "end of error list ");

                throw; // this will force the actor to be activated somewhere else. 
            }
        }

        #endregion
    }
}