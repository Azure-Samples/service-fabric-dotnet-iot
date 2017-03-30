using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Iot.Mocks
{
    public class MockApplicationLifetime : IApplicationLifetime
    {
        private readonly CancellationTokenSource source;

        public MockApplicationLifetime()
        {
            this.source = new CancellationTokenSource();
            this.ApplicationStopping = this.source.Token;
            this.ApplicationStopped = this.source.Token;
        }

        public CancellationToken ApplicationStarted { get; private set; }

        public CancellationToken ApplicationStopped { get; private set; }

        public CancellationToken ApplicationStopping { get; private set; }

        public void StopApplication()
        {
            this.source.Cancel();
        }
    }
}
