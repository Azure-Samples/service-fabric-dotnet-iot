// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    internal class DeferredTaskExecuter
    {
        public Action<AggregateException> OnError = (ae) =>
        {
            ae.Flatten();
            Trace.WriteLine(
                string.Format(
                    "Deferred task executer encountered error:{0} stacktrace:{1}",
                    ae.GetCombinedExceptionMessage(),
                    ae.GetCombinedExceptionStackTrace()));
        };

        private ConcurrentQueue<Func<Task>> m_Tasks = new ConcurrentQueue<Func<Task>>();
        private Task m_ExecutionTask;
        private CancellationTokenSource m_TokenSource = new CancellationTokenSource();
        private bool m_bAcceptTasks = true;

        public DeferredTaskExecuter()
        {
            this.NoTaskDelayMs = 1000;
            this.m_ExecutionTask = this.Work();
        }

        public uint NoTaskDelayMs { get; set; }

        public bool AddWork(Func<Task> func)
        {
            if (!this.m_bAcceptTasks)
            {
                return this.m_bAcceptTasks;
            }

            this.m_Tasks.Enqueue(func);


            return true;
        }

        public void Stop()
        {
            this.m_TokenSource.Cancel();
        }

        public void DrainStop()
        {
            this.m_bAcceptTasks = false;
        }

        public void Restart()
        {
            this.m_bAcceptTasks = true;
            this.m_TokenSource = new CancellationTokenSource();
        }

        private async Task Work()
        {
            while (!this.m_TokenSource.IsCancellationRequested)
            {
                try
                {
                    Func<Task> func;
                    bool bfound = this.m_Tasks.TryDequeue(out func);
                    if (!bfound)
                    {
                        await Task.Delay(1000);
                    }
                    else
                    {
                        await func();
                    }

                    if (!this.m_bAcceptTasks)
                    {
                        this.m_TokenSource.Cancel();
                    }
                }
                catch (AggregateException ae)
                {
                    this.OnError(ae);
                }
            }
        }
    }
}