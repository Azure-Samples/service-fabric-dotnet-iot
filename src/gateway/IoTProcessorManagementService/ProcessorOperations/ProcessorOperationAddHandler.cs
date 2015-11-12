// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Fabric;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using IoTProcessorManagement.Common;
    using Microsoft.ServiceFabric.Data;

    public class ProcessorOperationAddHandler : ProcessorOperationHandlerBase
    {
        public ProcessorOperationAddHandler(
            ProcessorManagementService svc,
            ProcessorOperation Opeartion) : base(svc, Opeartion)
        {
        }

        public override async Task RunOperation(ITransaction tx)
        {
            Processor processor = await this.GetProcessorAsync(this.processorOperation.ProcessorName);
            // if there is no default types names assigned, get defaults
            if (string.IsNullOrEmpty(processor.ServiceFabricAppTypeName))
            {
                processor.ServiceFabricAppTypeName = this.Svc.Config.AppTypeName;
                processor.ServiceFabricAppTypeVersion = this.Svc.Config.AppTypeVersion;

                processor.ServiceFabricAppInstanceName = string.Concat(
                    "fabric:/",
                    this.Svc.Config.AppInstanceNamePrefix,
                    this.Svc.Config.AppTypeName,
                    DateTime.UtcNow.Ticks
                    );

                processor.ServiceFabricServiceName = string.Concat(
                    processor.ServiceFabricAppInstanceName,
                    "/",
                    this.Svc.Config.ServiceTypeName);
            }

            // save it so we won't lose it
            await this.UpdateProcessorAsync(processor, null, true, true);
            try
            {
                await this.CreateAppAsync(processor);
                await this.CreateServiceAsync(processor);
                processor.ProcessorStatus &= ~ProcessorStatus.New;
                processor.ProcessorStatus = ProcessorStatus.Provisioned;
            }
            catch (FabricElementAlreadyExistsException elementExistEx)
            {
                string sMessage =
                    string.Format(
                        "An Add processor Operation has failed, application was partially created before {0}. overall the Add Processor operation was sucessful ",
                        elementExistEx.Message);
                ServiceEventSource.Current.Message(sMessage);
            }
            catch (FabricElementNotFoundException elementNotFoundEx)
            {
                processor.ProcessorStatus |= ProcessorStatus.ProvisionError;
                processor.ErrorMessage = string.Format(
                    "Failed to create service fabric app {0} [{1}-{2}] Error:{3}",
                    processor.ServiceFabricAppInstanceName,
                    processor.ServiceFabricAppTypeName,
                    processor.ServiceFabricAppTypeVersion,
                    elementNotFoundEx.Message);

                ServiceEventSource.Current.Message("Processor creation {0} failed - {1}", processor.Name, processor.ErrorMessage);
            }
            catch (AggregateException aex)
            {
                // did we try enough?
                if (!await this.ReEnqueAsync(tx))
                {
                    AggregateException ae = aex.Flatten();
                    processor.ProcessorStatus |= ProcessorStatus.ProvisionError;
                    processor.ErrorMessage = string.Format(
                        "Failed to create service fabric app {0} [{1}-{2}] Error:{3} \n after{4} times",
                        processor.ServiceFabricAppInstanceName,
                        processor.ServiceFabricAppTypeName,
                        processor.ServiceFabricAppTypeVersion,
                        ae.GetCombinedExceptionMessage(),
                        this.processorOperation.RetryCount - 1);

                    ServiceEventSource.Current.Message("Processor creation {0} failed - {1}", processor.Name, processor.ErrorMessage);

                    await this.CleanUpServiceFabricCluster(processor);
                }
            }
            finally
            {
                await this.UpdateProcessorAsync(processor, null, true, true);
            }


            ServiceEventSource.Current.Message("Processor creation {0} done", processor.Name);
        }

        public override Task<T> ExecuteOperation<T>(ITransaction tx)
        {
            // add operation does not support return values. 
            throw new NotImplementedException();
        }
    }
}