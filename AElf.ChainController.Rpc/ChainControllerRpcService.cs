﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using AElf.ChainController.CrossChain;
using AElf.ChainController.EventMessages;
using AElf.Kernel;
using AElf.Common;
using AElf.Configuration.Config.Chain;
using AElf.Database;
using AElf.Kernel.Managers;
using AElf.Kernel.Types;
using AElf.Miner.TxMemPool;
using AElf.Node.AElfChain;
using AElf.RPC;
using AElf.SmartContract;
using AElf.SmartContract.Consensus;
using AElf.SmartContract.Proposal;
using AElf.Synchronization.BlockSynchronization;
using Community.AspNetCore.JsonRpc;
using Easy.MessageHub;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Google.Protobuf;
using NLog;
using Transaction = AElf.Kernel.Transaction;

namespace AElf.ChainController.Rpc
{
    [Path("/chain")]
    public class ChainControllerRpcService : IJsonRpcService
    {
        #region Properties

        public IChainService ChainService { get; set; }
        public IChainContextService ChainContextService { get; set; }
        public IChainCreationService ChainCreationService { get; set; }
        public ITxHub TxHub { get; set; }
        public ITransactionResultService TransactionResultService { get; set; }
        public ITransactionTraceManager TransactionTraceManager { get; set; }
        public ISmartContractService SmartContractService { get; set; }
        public INodeService MainchainNodeService { get; set; }
        public ICrossChainInfoReader CrossChainInfoReader { get; set; }
        public IAuthorizationInfoReader AuthorizationInfoReader { get; set; }
        public IKeyValueDatabase KeyValueDatabase { get; set; }
        public IBlockSynchronizer BlockSynchronizer { get; set; }
        public IBinaryMerkleTreeManager BinaryMerkleTreeManager { get; set; }
        public IElectionInfo ElectionInfo { get; set; }

        #endregion Properties

        private readonly ILogger _logger;

        private bool _canBroadcastTxs = true;

        public ChainControllerRpcService(ILogger logger)
        {
            _logger = logger;

            MessageHub.Instance.Subscribe<ReceivingHistoryBlocksChanged>(msg => _canBroadcastTxs = !msg.IsReceiving);
        }
        
        #region Methods

        [JsonRpcMethod("get_commands")]
        public async Task<JObject> ProcessGetCommands()
        {
            try
            {
                var methodContracts = this.GetRpcMethodContracts();
                var commands = methodContracts.Keys.OrderBy(x => x).ToList();
                var json = JsonConvert.SerializeObject(commands);
                var arrCommands = JArray.Parse(json);
                var response = new JObject
                {
                    ["result"] = new JObject
                    {
                        ["commands"] = arrCommands
                    }
                };
                return await Task.FromResult(JObject.FromObject(response));
            }
            catch (Exception e)
            {
                return await Task.FromResult(new JObject
                {
                    ["error"] = e.ToString()
                });
            }
        }

        [JsonRpcMethod("connect_chain")]
        public async Task<JObject> ProGetChainInfo()
        {
            try
            {
                var basicContractZero =
                    ContractHelpers.GetGenesisBasicContractAddress(Hash.LoadBase58(ChainConfig.Instance.ChainId));
                var crosschainContract =
                    ContractHelpers.GetCrossChainContractAddress(Hash.LoadBase58(ChainConfig.Instance.ChainId));
                var authorizationContract =
                    ContractHelpers.GetAuthorizationContractAddress(Hash.LoadBase58(ChainConfig.Instance.ChainId));
                var tokenContract = ContractHelpers.GetTokenContractAddress(Hash.LoadBase58(ChainConfig.Instance.ChainId));
                var consensusContract = ContractHelpers.GetConsensusContractAddress(Hash.LoadBase58(ChainConfig.Instance.ChainId));
                var dividendsContract = ContractHelpers.GetDividendsContractAddress(Hash.LoadBase58(ChainConfig.Instance.ChainId));

                //var tokenContract = this.GetGenesisContractHash(SmartContractType.TokenContract);
                var response = new JObject
                {
                    ["result"] =
                        new JObject
                        {
                            [GlobalConfig.GenesisSmartContractZeroAssemblyName] = basicContractZero.GetFormatted(),
                            [GlobalConfig.GenesisCrossChainContractAssemblyName] = crosschainContract.GetFormatted(),
                            [GlobalConfig.GenesisAuthorizationContractAssemblyName] = authorizationContract.GetFormatted(),
                            [GlobalConfig.GenesisTokenContractAssemblyName] = tokenContract.GetFormatted(),
                            [GlobalConfig.GenesisConsensusContractAssemblyName] = consensusContract.GetFormatted(),
                            [GlobalConfig.GenesisDividendsContractAssemblyName] = dividendsContract.GetFormatted(),
                            ["chain_id"] = ChainConfig.Instance.ChainId
                        }
                };

                return await Task.FromResult(JObject.FromObject(response));
            }
            catch (Exception e)
            {
                var response = new JObject
                {
                    ["exception"] = e.ToString()
                };

                return await Task.FromResult(JObject.FromObject(response));
            }
        }

        [JsonRpcMethod("get_contract_abi", "address")]
        public async Task<JObject> ProcessGetContractAbi(string address)
        {
            try
            {
                var addrHash =Address.Parse(address);

                var abi = await this.GetContractAbi(addrHash);

                return new JObject
                {
                    ["address"] = address,
                    ["abi"] = abi.ToByteArray().ToHex(),
                    ["error"] = ""
                };
            }
            catch (Exception)
            {
                return new JObject
                {
                    ["address"] = address,
                    ["abi"] = "",
                    ["error"] = "Not Found"
                };
            }
        }

        [JsonRpcMethod("call", "rawtx")]
        public async Task<JObject> ProcessCallReadOnly(string raw64)
        {
            var hexString = ByteArrayHelpers.FromHexString(raw64);
            var transaction = Transaction.Parser.ParseFrom(hexString);

            JObject response;
            try
            {
                var res = await this.CallReadOnly(transaction);
                response = new JObject
                {
                    ["return"] = res?.ToHex()
                };
            }
            catch (Exception e)
            {
                response = new JObject
                {
                    ["error"] = e.ToString()
                };
            }

            return JObject.FromObject(response);
        }

        [JsonRpcMethod("broadcast_tx", "rawtx")]
        public async Task<JObject> ProcessBroadcastTx(string raw64)
        {
            var hexString = ByteArrayHelpers.FromHexString(raw64);
            var transaction = Transaction.Parser.ParseFrom(hexString);

            var res = new JObject {["hash"] = transaction.GetHash().ToHex()};

            if (!_canBroadcastTxs)
            {
                res["error"] = "Sync still in progress, cannot send transactions.";
                return res;
            }
            
            try
            {
                //TODO: Wait validation done
                transaction.GetTransactionInfo();
                await TxHub.AddTransactionAsync(transaction);
            }
            catch (Exception e)
            {
                res["error"] = e.ToString();
            }

            return res;
        }

        [JsonRpcMethod("broadcast_txs", "rawtxs")]
        public async Task<JObject> ProcessBroadcastTxs(string rawtxs)
        {
            var response = new List<object>();
            
            if (!_canBroadcastTxs)
            {
                return new JObject
                {
                    ["result"] = JToken.FromObject(string.Empty),
                    ["error"] = "Sync still in progress, cannot send transactions."
                };
            }

            foreach (var rawtx in rawtxs.Split(','))
            {
                var result = await ProcessBroadcastTx(rawtx);
                if (result.ContainsKey("error"))
                    break;
                response.Add(result["hash"].ToString());
            }

            return new JObject
            {
                ["result"] = JToken.FromObject(response)
            };
        }

        [JsonRpcMethod("get_merkle_path", "txid")]
        public async Task<JObject> ProcGetTxMerklePath(string txid)
        {
            try
            {
                Hash txHash;
                try
                {
                    txHash = Hash.LoadHex(txid);
                }
                catch (Exception)
                {
                    throw new Exception("Invalid Address Format");
                }
                var txResult = await this.GetTransactionResult(txHash);
                if(txResult.Status != Status.Mined)
                   throw new Exception("Transaction is not mined.");
                var binaryMerkleTree = await this.GetBinaryMerkleTreeByHeight(txResult.BlockNumber);
                var merklePath = binaryMerkleTree.GenerateMerklePath(txResult.Index);
                if(merklePath == null)
                    throw new Exception("Not found merkle path for this transaction.");
                MerklePath merklePathInParentChain = null;
                ulong boundParentChainHeight = 0;
                try
                {
                    merklePathInParentChain = await this.GetTxRootMerklePathInParentChain(txResult.BlockNumber);
                    boundParentChainHeight = await this.GetBoundParentChainHeight(txResult.BlockNumber);
                }
                catch (Exception e)
                {
                    throw new Exception($"Unable to get merkle path from parent chain {e}");
                }
                /*if(merklePathInParentChain == null)
                    throw new Exception("Not found merkle path in parent chain");*/
                if(merklePathInParentChain != null)
                    merklePath.Path.AddRange(merklePathInParentChain.Path);
                return new JObject
                {
                    ["merkle_path"] = merklePath.ToByteArray().ToHex(),
                    ["parent_height"] = boundParentChainHeight
                };
            }
            catch (Exception e)
            {
                return new JObject
                {
                    ["error"] = e.Message
                };
            }
        }
        
        [JsonRpcMethod("get_pcb_info", "height")]
        public async Task<JObject> ProcGetPCB(string height)
        {
            try
            {
                ulong h;
                try
                {
                    h = ulong.Parse(height);
                }
                catch (Exception)
                {
                    throw new Exception("Invalid height");
                }
                var merklePathInParentChain = await this.GetParentChainBlockInfo(h);
                if (merklePathInParentChain == null)
                {
                    throw new Exception("Unable to get parent chain block at height " + height);
                }

                return new JObject
                {
                    ["parent_chainId"] = merklePathInParentChain.Root.ChainId.DumpBase58(),
                    ["side_chain_txs_root"] = merklePathInParentChain.Root.SideChainTransactionsRoot.ToHex(),
                    ["parent_height"] = merklePathInParentChain.Height
                };
            }
            catch (Exception e)
            {
                return new JObject
                {
                    ["error"] = e.Message
                };
            }
        }
        
        [JsonRpcMethod("get_tx_result", "txhash")]
        public async Task<JObject> ProcGetTxResult(string txhash)
        {
            Hash txHash;
            try
            {
                txHash = Hash.LoadHex(txhash);
            }
            catch
            {
                return JObject.FromObject(new JObject
                {
                    ["error"] = "Invalid Format"
                });
            }

            try
            {
                var response = await GetTx(txHash);
                return JObject.FromObject(new JObject {["result"] = response});
            }
            catch (Exception e)
            {
                return new JObject
                {
                    ["error"] = e.Message
                };
            }
        }

        [JsonRpcMethod("get_txs_result", "blockhash", "offset", "num")]
        public async Task<JObject> GetTxsResult(string blockhash, int offset = 0, int num = 10)
        {
            if (offset < 0)
            {
                return JObject.FromObject(new JObject
                {
                    ["error"] = "offset must greater than or equal to 0."
                });
            }

            if (num<=0 || num > 100)
            {
                return JObject.FromObject(new JObject
                {
                    ["error"] = "num must between 0 and 100."
                });
            }

            Hash blockHash;
            try
            {
                blockHash = Hash.LoadHex(blockhash);
            }
            catch
            {
                return JObject.FromObject(new JObject
                {
                    ["error"] = "Invalid Block Hash Format"
                });
            }

            try
            {
                var block = await this.GetBlock(blockHash);
                if (block == null)
                {
                    return JObject.FromObject(new JObject
                    {
                        ["error"] = "Invalid Block Hash"
                    });
                }
                var txs = new JArray();

                if (offset <= block.Body.Transactions.Count - 1)
                {
                    num = Math.Min(num, block.Body.Transactions.Count - offset);

                    var txHashs = block.Body.Transactions.ToList().GetRange(offset, num);
                    foreach (var hash in txHashs)
                    {
                        txs.Add(await GetTx(hash));
                    }
                }

                return JObject.FromObject(new JObject {["result"] = txs});
            }
            catch (Exception e)
            {
                return new JObject
                {
                    ["error"] = e.Message
                };
            }
        }

        private async Task<JObject> GetTx(Hash txHash)
        {
            var receipt = await this.GetTransactionReceipt(txHash);
            JObject txInfo = null;
            if (receipt != null)
            {
                var transaction = receipt.Transaction;
                txInfo = transaction.GetTransactionInfo();
                ((JObject) txInfo["tx"]).Add("params",
                    string.Join(", ", await this.GetTransactionParameters(transaction)));
                ((JObject) txInfo["tx"]).Add("SignatureState", receipt.SignatureSt.ToString());
                ((JObject) txInfo["tx"]).Add("RefBlockState", receipt.RefBlockSt.ToString());
                ((JObject) txInfo["tx"]).Add("ExecutionState", receipt.Status.ToString());
                ((JObject) txInfo["tx"]).Add("ExecutedInBlock", receipt.ExecutedBlockNumber);
            }
            else
            {
                txInfo = new JObject {["tx"] = "Not Found"};
            }

            var txResult = await this.GetTransactionResult(txHash);
            var response = new JObject
            {
                ["tx_status"] = txResult.Status.ToString(),
                ["tx_info"] = txInfo["tx"]
            };
#if DEBUG
            var txtrc = await this.GetTransactionTrace(txHash, txResult.BlockNumber);
            response["tx_trc"] = txtrc?.ToString();
#endif
            
            if (txResult.Status == Status.Failed)
            {
                response["tx_error"] = txResult.RetVal.ToStringUtf8();
            }
            
            if (txResult.Status == Status.Mined)
            {
                response["block_number"] = txResult.BlockNumber;
                response["block_hash"] = txResult.BlockHash.ToHex();
#if DEBUG
                response["return_type"] = txtrc.RetVal.Type.ToString();
#endif
                try
                {
                    response["return"] = Address.FromBytes(txResult.RetVal.ToByteArray()).GetFormatted();

                }
                catch (Exception)
                {
                    // not an error
                    response["return"] = txResult.RetVal.ToByteArray().ToHex();
                }
            }
            // Todo: it should be deserialized to obj ion cli, 

            return response;
        }

        [JsonRpcMethod("get_block_height")]
        public async Task<JObject> ProGetBlockHeight()
        {
            var height = await this.GetCurrentChainHeight();
            var response = new JObject
            {
                ["result"] = new JObject
                {
                    ["block_height"] = height.ToString()
                }
            };
            return JObject.FromObject(response);
        }

        [JsonRpcMethod("get_block_info", "block_height", "include_txs")]
        public async Task<JObject> ProGetBlockInfo(string blockHeight, bool includeTxs = false)
        {
            var invalidBlockHeightError = JObject.FromObject(new JObject
            {
                ["error"] = "Invalid Block Height"
            });

            if (!ulong.TryParse(blockHeight, out var height))
            {
                return invalidBlockHeightError;
            }

            var blockinfo = await this.GetBlockAtHeight(height);
            if (blockinfo == null)
                return invalidBlockHeightError;

            // TODO: Create DTO Exntension for Block
            var response = new JObject
            {
                ["result"] = new JObject
                {
                    ["Blockhash"] = blockinfo.GetHash().ToHex(),
                    ["Header"] = new JObject
                    {
                        ["PreviousBlockHash"] = blockinfo.Header.PreviousBlockHash.ToHex(),
                        ["MerkleTreeRootOfTransactions"] = blockinfo.Header.MerkleTreeRootOfTransactions.ToHex(),
                        ["MerkleTreeRootOfWorldState"] = blockinfo.Header.MerkleTreeRootOfWorldState.ToHex(),
                        ["SideChainTransactionsRoot"] = blockinfo.Header.SideChainTransactionsRoot?.ToHex(),
                        ["Index"] = blockinfo.Header.Index.ToString(),
                        ["Time"] = blockinfo.Header.Time.ToDateTime(),
                        ["ChainId"] = blockinfo.Header.ChainId.DumpBase58(),
                        //["IndexedInfo"] = blockinfo.Header.GetIndexedSideChainBlcokInfo()
                    },
                    ["Body"] = new JObject
                    {
                        ["TransactionsCount"] = blockinfo.Body.TransactionsCount,
                        ["IndexedSideChainBlcokInfo"] = blockinfo.GetIndexedSideChainBlockInfo()
                    }
                }
            };

            if (includeTxs)
            {
                var transactions = blockinfo.Body.Transactions;
                var txs = new List<string>();
                foreach (var txHash in transactions)
                {
                    txs.Add(txHash.ToHex());
                }

                response["result"]["Body"]["Transactions"] = JArray.FromObject(txs);
            }

            return JObject.FromObject(response);
        }

        [JsonRpcMethod("get_txpool_size")]
        public async Task<JObject> GetTxPoolSize()
        {
            var transactionPoolSize = await this.GetTransactionPoolSize();
            var response = new JObject
            {
                ["CurrentTransactionPoolSize"] = transactionPoolSize
            };

            return JObject.FromObject(response);
        }
        
        [JsonRpcMethod("dpos_isalive")]
        public async Task<JObject> IsDPoSAlive()
        {
            var isAlive = await MainchainNodeService.CheckDPoSAliveAsync();
            var response = new JObject
            {
                ["IsAlive"] = isAlive
            };

            return JObject.FromObject(response);
        }
        
        [JsonRpcMethod("node_isforked")]
        public async Task<JObject> NodeIsForked()
        {
            var isForked = await MainchainNodeService.CheckForkedAsync();
            var response = new JObject
            {
                ["IsForked"] = isForked
            };

            return JObject.FromObject(response);
        }
        
        #endregion Methods

        #region Proposal
        [JsonRpcMethod("check_proposal", "proposal_id")]
        public async Task<JObject> ProcGetProposal(string proposalId)
        {
            try
            {
                Hash proposalHash;
                try
                {
                    proposalHash = Hash.LoadHex(proposalId);
                }
                catch (Exception)
                {
                    throw new Exception("Invalid Hash Format");
                }

                var proposal = await this.GetProposal(proposalHash);
                var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                return new JObject
                {
                    ["result"] = new JObject
                    {
                        ["proposal_name"] = proposal.Name,
                        ["multi_sig"] = proposal.MultiSigAccount.GetFormatted(),
                        ["expired_time"] = origin.AddSeconds(proposal.ExpiredTime),
                        ["TxnData"] = proposal.TxnData.ToByteArray().ToHex(),
                        ["status"] = proposal.Status.ToString(),
                        ["proposer"] = proposal.Proposer.GetFormatted()
                    }
                };
            }
            catch (Exception e)
            {
                return new JObject
                {
                    ["error"] = e.Message
                };
            }
        }
        #endregion
        
        #region Consensus

        public async Task<JObject> VotesGeneral()
        {
            try
            {
                var general = await this.GetVotesGeneral();
                return new JObject
                {
                    ["error"] = 0,
                    ["voters_count"] = general.Item1,
                    ["tickets_count"] = general.Item2,
                };
            }
            catch (Exception e)
            {
                return new JObject
                {
                    ["error"] = 1,
                    ["errormsg"] = e.Message
                };
            }
        }

        #endregion

        #region Admin
        
        [JsonRpcMethod("get_invalid_block")]
        public async Task<JObject> InvalidBlockCount()
        {
            var invalidBlockCount = await this.GetInvalidBlockCountAsync();
                
            var response = new JObject
            {
                ["InvalidBlockCount"] = invalidBlockCount
            };

            return JObject.FromObject(response);
        }
        
        [JsonRpcMethod("get_rollback_times")]
        public async Task<JObject> RollBackTimes()
        {
            var rollBackTimes = await this.GetRollBackTimesAsync();
                
            var response = new JObject
            {
                ["RollBackTimes"] = rollBackTimes
            };

            return JObject.FromObject(response);
        }

        [JsonRpcMethod("get_db_value","key")]
        public async Task<JObject> GetDbValue(string key)
        {
            string type = string.Empty;
            JToken id;
            try
            {
                object value;

                if (key.StartsWith(GlobalConfig.StatePrefix))
                {
                    type = "State";
                    id = key.Substring(GlobalConfig.StatePrefix.Length, key.Length - GlobalConfig.StatePrefix.Length);
                    var valueBytes = await KeyValueDatabase.GetAsync(type,key);
                    value = StateValue.Create(valueBytes);
                }
                else if(key.StartsWith(GlobalConfig.TransactionReceiptPrefix))
                {
                    type = "TransactionReceipt";
                    id = key.Substring(GlobalConfig.TransactionReceiptPrefix.Length, key.Length - GlobalConfig.TransactionReceiptPrefix.Length);
                    var valueBytes = await KeyValueDatabase.GetAsync(type,key);
                    value = valueBytes?.Deserialize<TransactionReceipt>();
                }
                else
                {
                    var keyObj = Key.Parser.ParseFrom(ByteArrayHelpers.FromHexString(key));
                    type = keyObj.Type;
                    id = JObject.Parse(keyObj.ToString());
                    var valueBytes = await KeyValueDatabase.GetAsync(type,key);
                    var obj = this.GetInstance(type);
                    obj.MergeFrom(valueBytes);
                    value = obj;
                }

                var response = new JObject
                {
                    ["Type"] = type,
                    ["Id"] = id,
                    ["Value"] = JObject.Parse(value?.ToString())
                };

                return JObject.FromObject(response);
            }
            catch (Exception e)
            {
                var response = new JObject
                {
                    ["Type"]=type,
                    ["Value"] = e.Message
                };
                return JObject.FromObject(response);
            }
        }

        #endregion Methods
    }
}