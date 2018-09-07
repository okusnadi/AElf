﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.ChainController;
using AElf.Common.Attributes;
using AElf.Kernel;
using Grpc.Core;
using NLog;

namespace AElf.Miner.Rpc.Server
{
    [LoggerName("MinerServer")]
    public class HeaderInfoServerImpl : HeaderInfoRpc.HeaderInfoRpcBase
    {
        private readonly IChainService _chainService;
        private readonly ILogger _logger;
        private ILightChain LightChain { get; set; }

        public HeaderInfoServerImpl(IChainService chainService, ILogger logger)
        {
            _chainService = chainService;
            _logger = logger;
        }

        public void Init(Hash chainId)
        {
            LightChain = _chainService.GetLightChain(chainId);
        }
        //
        public override async Task Index(IAsyncStreamReader<RequestSideChainIndexedInfo> requestStream, 
            IServerStreamWriter<ResponseSideChainIndexedInfo> responseStream, ServerCallContext context)
        {
            // TODO: verify the from address and the chain 
            _logger?.Log(LogLevel.Debug, "Server received IndexedInfo message.");

            try
            {
                while (await requestStream.MoveNext())
                {
                    var requestInfo = requestStream.Current;
                    var requestedHeight = requestInfo.NextHeight;
                    var blockHeader = await LightChain.GetHeaderByHeightAsync(requestedHeight);
                    var res = new ResponseSideChainIndexedInfo
                    {
                        Height = requestedHeight,
                        BlockHeaderHash = blockHeader?.GetHash(),
                        TransactionMKRoot = blockHeader?.MerkleTreeRootOfTransactions,
                        Success = blockHeader != null,
                        ChainId = blockHeader?.ChainId
                    };
                    _logger?.Log(LogLevel.Debug, $"Server responsed IndexedInfo message of height {requestedHeight}");
                    await responseStream.WriteAsync(res);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }
    }
}