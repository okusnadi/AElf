using AElf.Common;
using AElf.Common.Serializers;
using AElf.Database;

namespace AElf.Kernel.Storages
{
    public class TransactionResultStore : KeyValueStoreBase, ITransactionResultStore
    {
        public TransactionResultStore(IKeyValueDatabase keyValueDatabase, IByteSerializer byteSerializer)
            : base(keyValueDatabase, byteSerializer, GlobalConfig.TransactionResultPrefix)
        {
        }
    }
}
