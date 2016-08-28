// ------------------------------------------------------------
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Mocks
{
    using Collections = System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;

    /// <summary>
    /// Simple wrapper for a synchronous IEnumerable of T.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class MockAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private Collections.IEnumerable<T> enumerable;

        public MockAsyncEnumerable(Collections.IEnumerable<T> enumerable)
        {
            this.enumerable = enumerable;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator()
        {
            return new MockAsyncEnumerator<T>(this.enumerable.GetEnumerator());
        }
    }

    /// <summary>
    /// Simply wrapper for a synchronous IEnumerator of T.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class MockAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly Collections.IEnumerator<T> enumerator;

        public MockAsyncEnumerator(Collections.IEnumerator<T> enumerator)
        {
            this.enumerator = enumerator;
        }

        public T Current
        {
            get { return this.enumerator.Current; }
        }

        public void Dispose()
        {
            this.enumerator.Dispose();
        }

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(this.enumerator.MoveNext());
        }

        public void Reset()
        {
            this.enumerator.Reset();
        }
    }
}