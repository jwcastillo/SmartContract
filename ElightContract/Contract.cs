﻿using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;

namespace ElightContract
{
    //represents a real contract between carrier and client, which describes conditions like
    //temperature, humidity and etc. It's impossible to create new neo smart contract for each 
    //shipping (490 gas), so we decided to write them as byte code and interpret using interpreter object
    public struct Contract
    {
        public enum Option
        {
            WithoutDeposit = 0,
            WithDeposit = 1
        }

        public enum STATUS
        {
            ACTIVE = 0,       //hasn't been executed yet
            SUCCESS,          //has been executed, result lays in specified borders
            FAILURE,          //has been executed, result doesn't in specified borders
            EXECUTION_ERROR   //has executed with errors
        }

        public STATUS Status;
        public byte[] Info;       //additional data, for example, author, description etc
        public byte[] Conditions; //byte code for interpreter
        public Deposit Deposit;
        public Option ContractOption;

        private static string GetContractKey(string carrierHash, BigInteger index)
        {
            string main = Prefixes.CONTRACT_PREFIX + carrierHash;
            return main + index;
        }

        private static string GetContractCounterKey(string carrierHash)
        {
            return Prefixes.CONTRACT_COUNTER_PREFIX + carrierHash;
        }

        private static BigInteger GetContractCounter(string carrierHash)
        {
            string contractCounterKey = GetContractCounterKey(carrierHash);
            byte[] contractCounter = Storage.Get(Storage.CurrentContext, contractCounterKey);
            return (contractCounter.Length == 0) ? 0 : contractCounter.AsBigInteger();
        }

        private static void PutContractCounter(string carrierHash, BigInteger contractCounter)
        {
            string contractCounterKey = GetContractCounterKey(carrierHash);
            Storage.Put(Storage.CurrentContext, contractCounterKey, contractCounter);
        }

        public static Contract InitDeposit(Contract contract, byte[] carrierHash, 
            byte[] clientHash, BigInteger amount)
        {
            contract.Deposit = Deposit.Init(carrierHash, clientHash, amount);
            
            if (Deposit.Freeze(contract.Deposit))
            {
                //deposit has been assigned successfuly
                contract.ContractOption = Option.WithDeposit;
            }

            return contract;
        }

        public static Contract Init(byte[] info, byte[] source)
        {
            return new Contract
            {
                Conditions = source,
                Status = STATUS.ACTIVE,
                Info = info,
                ContractOption = Option.WithoutDeposit
            };
        }

        public static Contract GetContract(string carrierHash, BigInteger index)
        {
            string contractKey = GetContractKey(carrierHash, index);
            byte[] contract = Storage.Get(Storage.CurrentContext, contractKey);
            Runtime.Notify(contract);
            Runtime.Notify("Contract");

            return (Contract)contract;
        }
        
        //store contract in blockchain 
        public static bool PutContract(Contract contract, string carrierHash)
        {
            Runtime.Notify("PutContract");
            if (!Runtime.CheckWitness(carrierHash.AsByteArray()))
            {
                Runtime.Notify("Invalid witness");
                return false;
            }

            BigInteger contractCounter = GetContractCounter(carrierHash);
            contractCounter += 1;
            Runtime.Notify("Counter");
            Runtime.Notify(contractCounter);

            string contractCounterKey = GetContractCounterKey(carrierHash);
            string contractKey = GetContractKey(carrierHash, contractCounter);
            Runtime.Notify(contractCounterKey);
            Runtime.Notify(contractKey);

            Storage.Put(Storage.CurrentContext, contractCounterKey, contractCounter);
            Storage.Put(Storage.CurrentContext, contractKey, (byte[])contract);
            return true;
        }
        
        private static bool UpdateContract(Contract contract, string carrierHash)
        {
            Runtime.Notify("UpdateContract");
            if (!Runtime.CheckWitness(carrierHash.AsByteArray()))
            {
                Runtime.Notify("Invalid witness");
                return false;
            }

            BigInteger contractCounter = GetContractCounter(carrierHash);
            Runtime.Notify("Counter");
            Runtime.Notify(contractCounter);

            string contractCounterKey = GetContractCounterKey(carrierHash);
            string contractKey = GetContractKey(carrierHash, contractCounter);
            Runtime.Notify(contractCounterKey);
            Runtime.Notify(contractKey);

            Storage.Put(Storage.CurrentContext, contractCounterKey, contractCounter);
            return true;
        }

        //after a propgram has been executed, its status should be changed
        //so we sure that it's not going to be executed one more time
        public static Contract ChangeStatus(Contract contract, BigInteger i, string carrierHash, STATUS status)
        {
            contract.Status = status;
            string contractKey = GetContractKey(carrierHash, i);
            Storage.Put(Storage.CurrentContext, contractKey, (byte[])contract);
            return contract;
        }

        //Type conversations to make possible to store Contract structure in blockchain
        //[STATUS][INFO LENGTH][INFO DATA][SRC LENGTH][SRC DATA]
        //[4 byte][  4 bytes  ][ arbitary][ 4 bytes  ][arbitary]
        public static explicit operator byte[] (Contract contract)
        {
            BigInteger status = ((Int32)contract.Status);
            byte[] res = ((Int32)contract.Status).ToByteArray()
                .Concat(contract.Info.Length.ToByteArray())
                .Concat(contract.Info)
                .Concat(contract.Conditions.Length.ToByteArray())
                .Concat(contract.Conditions)
                .Concat(((Int32)contract.ContractOption).ToByteArray());

            if (contract.ContractOption == Option.WithDeposit)
            {
                res = res.Concat((byte[])contract.Deposit);
            }
            return res;
        }

        //[STATUS][INFO LENGTH][INFO DATA][SRC LENGTH][SRC DATA][ Deposit ]
        //[4 byte][  4 bytes  ][ arbitary][ 4 bytes  ][arbitary][ 40 bytes]
        public static explicit operator Contract(byte[] ba)
        {
            Int32 index = 0;
            STATUS status = (STATUS)ba.ToInt32(index, false);
            Runtime.Notify(status);

            index += 4;
            Int32 infoLen = ba.ToInt32(index, false);
            Runtime.Notify(infoLen);

            index += 4;
            byte[] info = ba.Range(index, infoLen);
            Runtime.Notify(info);

            index += infoLen;
            Int32 sourceLen = ba.ToInt32(index, false);
            Runtime.Notify(sourceLen);

            index += 4;
            byte[] source = ba.Range(index, sourceLen);
            Runtime.Notify(source);

            index += sourceLen;
            Option contractOption = (Option)ba.ToInt32(index, false);
            Runtime.Notify(contractOption);

            Contract contract = new Contract
            {
                Status = status,
                Conditions = source,
                Info = info,
                ContractOption = contractOption,
            };

            if (contractOption == Option.WithDeposit)
            {
                index += 4;
                Deposit deposit = (Deposit)ba.Range(index, Deposit.DepositSize);
                contract.Deposit = deposit;
            }

            return contract;
        }
    }
}
