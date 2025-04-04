// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Linq;
using Nethermind.Config;
using Nethermind.Core;

namespace Nethermind.KeyStore.Config;

/// <summary>
/// https://medium.com/@julien.maffre/what-is-an-ethereum-keystore-file-86c8c5917b97
/// https://github.com/ethereum/wiki/wiki/Web3-Secret-Storage-Definition
/// </summary>
public interface IKeyStoreConfig : IConfig
{
    [ConfigItem(Description = "The path to the keystore directory.", DefaultValue = "keystore")]
    string KeyStoreDirectory { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "UTF-8")]
    string KeyStoreEncoding { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "scrypt")]
    string Kdf { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "aes-128-ctr")]
    string Cipher { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "32")]
    int KdfparamsDklen { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "262144")]
    int KdfparamsN { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "1")]
    int KdfparamsP { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "8")]
    int KdfparamsR { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "32")]
    int KdfparamsSaltLen { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "128")]
    int SymmetricEncrypterBlockSize { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "128")]
    int SymmetricEncrypterKeySize { get; set; }

    [ConfigItem(Description = "See [Web3 secret storage definition][web3-secret-storage].", DefaultValue = "16")]
    int IVSize { get; set; }

    [ConfigItem(Description = "A plaintext private key to use for testing purposes.")]
    string TestNodeKey { get; set; }

    [ConfigItem(Description = "An account to use as the block author (coinbase).")]
    string BlockAuthorAccount { get; set; }

    [ConfigItem(Description = $"An account to use for networking (enode). If neither this nor the `{nameof(EnodeKeyFile)}` option is specified, the key is autogenerated in `node.key.plain` file.")]
    string EnodeAccount { get; set; }

    [ConfigItem(Description = $"The path to the key file to use by for networking (enode). If neither this nor the `{nameof(EnodeAccount)}` is specified, the key is autogenerated in `node.key.plain` file.")]
    string EnodeKeyFile { get; set; }

    [ConfigItem(Description = $"An array of passwords used to unlock the accounts set with `{nameof(UnlockAccounts)}`.", DefaultValue = "[]")]
    string[] Passwords { get; set; }

    [ConfigItem(Description = $"An array of password files paths used to unlock the accounts set with `{nameof(UnlockAccounts)}`.", DefaultValue = "[]")]
    string[] PasswordFiles { get; set; }

    [ConfigItem(Description = $"An array of accounts to unlock on startup using passwords either in `{nameof(PasswordFiles)}` and `{nameof(Passwords)}`.", DefaultValue = "[]")]
    string[] UnlockAccounts { get; set; }
}

public static class KeyStoreConfigExtensions
{
    public static int FindUnlockAccountIndex(this IKeyStoreConfig keyStoreConfig, Address address)
    {
        return Array.IndexOf(
            (keyStoreConfig.UnlockAccounts ?? [])
            .Select(static a => a.ToUpperInvariant())
            .ToArray(),
            address.ToString().ToUpperInvariant());
    }
}
