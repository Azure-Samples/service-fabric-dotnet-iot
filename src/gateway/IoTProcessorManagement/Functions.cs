// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement
{
    using System;
    using IoTProcessorManagement.Clients;

    public static class Functions
    {
#if DEBUG

        /*
        this sends test events to processor (event hubs) 
        it should be removed from deployment version
        */

        public static void SendProcessorTestMessages(Processor processor, int NumOfMessages, int NumOfPublishers)
        {
            try
            {
                InternalFunctions.SendTestEventsToProcessorHubsAsync(processor, NumOfMessages, NumOfPublishers).Wait();
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
        }
#endif

        public static Processor UpdateProcessor(string BaseAddress, string processorJson)
        {
            Processor processor = Processor.FromJsonString(processorJson);
            try
            {
                return InternalFunctions.UpdateProcessorAsync(BaseAddress, processor).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static Processor AddProcessor(string BaseAddress, string processorJson)
        {
            Processor processor = Processor.FromJsonString(processorJson);
            try
            {
                return InternalFunctions.AddProcessorAsync(BaseAddress, processor).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static string GetManagementApiEndPoint(string FabricEndPoint, string sMgmtAppInstanceName)
        {
            try
            {
                return InternalFunctions.GetManagementApiEndPointAsync(FabricEndPoint, sMgmtAppInstanceName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static Processor UpdateProcessor(string BaseAddress, Processor processor)
        {
            try
            {
                return InternalFunctions.UpdateProcessorAsync(BaseAddress, processor).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static Processor AddProcessor(string BaseAddress, Processor processor)
        {
            try
            {
                return InternalFunctions.AddProcessorAsync(BaseAddress, processor).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static Processor GetPrcossor(string BaseAddress, string ProcessorName)
        {
            try
            {
                return InternalFunctions.GetProcessorAsync(BaseAddress, ProcessorName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static Processor[] GetAllProcesseros(string BaseAddress)
        {
            try
            {
                return InternalFunctions.GetAllProcesserosAsync(BaseAddress).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static Processor DeleteProcessor(string BaseAddress, string processorName)
        {
            try
            {
                return InternalFunctions.DeleteProcessorAsync(BaseAddress, processorName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        #region Per Processor Action

        public static Processor StopProcessor(string BaseAddress, string processorName)
        {
            try
            {
                return InternalFunctions.StopProcessorAsync(BaseAddress, processorName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        public static Processor DrainStopProcessor(string BaseAddress, string processorName)
        {
            try
            {
                return InternalFunctions.DrainStopProcessorAsync(BaseAddress, processorName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }


        public static Processor PauseProcessor(string BaseAddress, string processorName)
        {
            try
            {
                return InternalFunctions.PauseProcessorAsync(BaseAddress, processorName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }


        public static Processor ResumeProcessor(string BaseAddress, string processorName)
        {
            try
            {
                return InternalFunctions.ResumeProcessorAsync(BaseAddress, processorName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }


        public static ProcessorRuntimeStatus[] GetDetailedProcessorStatus(string BaseAddress, string processorName)
        {
            try
            {
                return InternalFunctions.GetDetailedProcessorStatusAsync(BaseAddress, processorName).Result;
            }
            catch (AggregateException ae)
            {
                ae.ThrowPowerShell();
            }
            return null;
        }

        #endregion
    }
}