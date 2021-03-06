using System;
using Google.Protobuf;

namespace AElf.Common
{
    public static class ContractHelpers
    {
        public static string IndexingSideChainMethodName { get; } = "IndexSideChainBlockInfo";
        public static string IndexingParentChainMethodName { get; } = "IndexParentChainBlockInfo";
        public static Address GetSystemContractAddress(Hash chainId, UInt64 serialNumber)
        {
            return Address.BuildContractAddress(chainId, serialNumber);
        }
        
        public static Address GetGenesisBasicContractAddress(Hash chainId)
        {
            return Address.BuildContractAddress(chainId, GlobalConfig.GenesisBasicContract);
        }
        
        public static Address GetConsensusContractAddress(Hash chainId)
        {
            return Address.BuildContractAddress(chainId, GlobalConfig.ConsensusContract);
        }
        
        public static Address GetTokenContractAddress(Hash chainId)
        {
            return Address.BuildContractAddress(chainId, GlobalConfig.TokenContract);
        }
        
        public static Address GetCrossChainContractAddress(Hash chainId)
        {
            return Address.BuildContractAddress(chainId, GlobalConfig.CrossChainContract);
        }

        public static Address GetAuthorizationContractAddress(Hash chainId)
        {
            return Address.BuildContractAddress(chainId, GlobalConfig.AuthorizationContract);
        }

        public static Address GetResourceContractAddress(Hash chainId)
        {
            return Address.BuildContractAddress(chainId, GlobalConfig.ResourceContract);
        }

        public static Address GetDividendsContractAddress(Hash chainId)
        {
            return Address.BuildContractAddress(chainId, GlobalConfig.DividendsContract);
        }
    }
}