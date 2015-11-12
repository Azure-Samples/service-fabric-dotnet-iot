// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace EventHubProcessor
{
    using System.Threading.Tasks;
    using System.Web.Http;
    using IoTProcessorManagement.Clients;

    public class EventHubProcessorController : ApiController, IEventHubProcessorController
    {
        // this reference is set by dependancy injection built in OWIN pipeline
        public IoTEventHubProcessorService ProcessorService { get; set; }

        [HttpPost]
        [Route("eventhubprocessor/pause")]
        public async Task Pause()
        {
            this.ProcessorService.TraceWriter.TraceMessage("Recevied Pause Command");
            await this.ProcessorService.Pause();
            this.ProcessorService.TraceWriter.TraceMessage("Completed Pause Command");
        }

        [HttpPost]
        [Route("eventhubprocessor/stop")]
        public async Task Stop()
        {
            this.ProcessorService.TraceWriter.TraceMessage("Recevied Stop Command");
            await this.ProcessorService.Stop();
            this.ProcessorService.TraceWriter.TraceMessage("Completed Stop Command");
        }

        [HttpPost]
        [Route("eventhubprocessor/resume")]
        public async Task Resume()
        {
            this.ProcessorService.TraceWriter.TraceMessage("Recevied Resume Command");
            await this.ProcessorService.Resume();
            this.ProcessorService.TraceWriter.TraceMessage("Completed Resume Command");
        }

        [HttpPost]
        [Route("eventhubprocessor/drainstop")]
        public async Task DrainStop()
        {
            this.ProcessorService.TraceWriter.TraceMessage("Recevied Drain/Stop Command");
            await this.ProcessorService.DrainAndStop();
            this.ProcessorService.TraceWriter.TraceMessage("Completed Drain/Stop Command");
        }

        [HttpPut]
        [Route("eventhubprocessor/")]
        public async Task Update(Processor newProcessor)
        {
            await this.ProcessorService.SetAssignedProcessorAsync(newProcessor);
        }

        [HttpGet]
        [Route("eventhubprocessor/")]
        public async Task<ProcessorRuntimeStatus> getStatus()
        {
            this.ProcessorService.TraceWriter.TraceMessage("Recevied GetStatus Command");
            //scater gather status for each reading. 

            ProcessorRuntimeStatus status = new ProcessorRuntimeStatus();

            status.TotalPostedLastMinute = await this.ProcessorService.GetTotalPostedLastMinuteAsync();
            status.TotalProcessedLastMinute = await this.ProcessorService.GetTotalProcessedLastMinuteAsync();
            status.TotalPostedLastHour = await this.ProcessorService.GetTotalPostedLastHourAsync();
            status.TotalProcessedLastHour = await this.ProcessorService.GetTotalProcessedLastHourAsync();
            status.AveragePostedPerMinLastHour = await this.ProcessorService.GetAveragePostedPerMinLastHourAsync();
            status.AverageProcessedPerMinLastHour = await this.ProcessorService.GetAverageProcessedPerMinLastHourAsync();
            status.StatusString = await this.ProcessorService.GetStatusStringAsync();
            status.NumberOfActiveQueues = await this.ProcessorService.GetNumberOfActiveQueuesAsync();
            status.NumberOfBufferedItems = await this.ProcessorService.GetNumOfBufferedItemsAsync();


            status.IsInErrorState = this.ProcessorService.IsInErrorState;
            status.ErrorMessage = this.ProcessorService.ErrorMessage;

            this.ProcessorService.TraceWriter.TraceMessage("Completed get status Command");
            return status;
        }
    }
}