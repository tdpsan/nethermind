// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Db;
using Nethermind.Trie.Pruning;

namespace Nethermind.Trie;

public class NodeStorage(
    IKeyValueStore keyValueStore,
    INodeStorage.KeyScheme scheme = INodeStorage.KeyScheme.HalfPath,
    bool requirePath = true)
    : INodeStorage
{
    private readonly IKeyValueStore _keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
    private static readonly byte[] EmptyTreeHashBytes = [128];
    private const int StoragePathLength = 74;
    private const int TopStateBoundary = 5;

    public INodeStorage.KeyScheme Scheme { get; set; } = scheme;
    public bool RequirePath { get; } = requirePath;

    private Span<byte> GetExpectedPath(Span<byte> pathSpan, in ValueHash256? address, in TreePath path, in ValueHash256 keccak) =>
        Scheme == INodeStorage.KeyScheme.HalfPath
            ? GetHalfPathNodeStoragePathSpan(pathSpan, address, path, keccak)
            : GetHashBasedStoragePath(pathSpan, keccak);

    public static byte[] GetHalfPathNodeStoragePath(in ValueHash256? address, in TreePath path, in ValueHash256 keccak) =>
        GetHalfPathNodeStoragePathSpan(stackalloc byte[StoragePathLength], address, path, keccak).ToArray();

    private static Span<byte> GetHalfPathNodeStoragePathSpan(Span<byte> pathSpan, in ValueHash256? address, in TreePath path, in ValueHash256 keccak)
    {
        Debug.Assert(pathSpan.Length == StoragePathLength);

        // Key structure look like this.
        //
        // For state (total 42 byte)
        //
        // +--------------+------------------+------------------+--------------+
        // | section byte | 8 byte from path | path length byte | 32 byte hash |
        // +--------------+------------------+------------------+--------------+
        //
        // For storage (total 74 byte)
        // +--------------+---------------------+------------------+------------------+--------------+
        // | section byte | 32 byte from address | 8 byte from path | path length byte | 32 byte hash |
        // +--------------+---------------------+------------------+------------------+--------------+
        //
        // The section byte is:
        // - 0 if state and path length is <= 5.
        // - 1 if state and path length is > 5.
        // - 2 if storage.
        //
        // The keys are separated due to the different characteristics of these nodes. The idea being that top level
        // node can be up to 5 times bigger than lower node, and grew a lot due to pruning. So mixing them makes lower
        // node sparser and have poorer cache hit, and make traversing leaves for snap serving slower.
        //
        // Technically, you'll need 9 byte for state and 8 byte for storage on mainnet for the path. But we want to keep
        // key small at the same time too. If the key are too small, multiple node will be out of order, which
        // can be slower but as long as they are in the same data block, it should not make a difference.
        // On mainnet, the out-of-order key is around 0.03% for address and 0.07% for storage.

        if (address is null)
        {
            // Separate the top level tree into its own section. This marginally improve cache hit rate, but not much.
            // 70% of duplicated keys is in this section, making them pretty bad, so we isolate them here to not expand
            // the space of other things, hopefully we can cache them by key somehow. Separating by the path length 4
            // does improve cache hit and processing time a little bit, until a few hundreds prune persist where it grew
            // beyond block cache size.
            pathSpan[0] = path.Length <= TopStateBoundary ? (byte)0 : (byte)1;

            // Keep key small
            path.Path.BytesAsSpan[..8].CopyTo(pathSpan[1..]);
            keccak.Bytes.CopyTo(pathSpan[10..]);

            pathSpan[9] = (byte)path.Length;
            return pathSpan[..42];
        }
        else
        {
            pathSpan[0] = 2;
            address.Value.Bytes.CopyTo(pathSpan[1..]);
            path.Path.BytesAsSpan[..8].CopyTo(pathSpan[33..]);

            pathSpan[41] = (byte)path.Length;
            keccak.Bytes.CopyTo(pathSpan[42..]);
            return pathSpan;
        }

    }

    private static Span<byte> GetHashBasedStoragePath(Span<byte> pathSpan, in ValueHash256 keccak)
    {
        Debug.Assert(pathSpan.Length == StoragePathLength);
        keccak.Bytes.CopyTo(pathSpan);
        return pathSpan[..32];
    }

    public byte[]? Get(Hash256? address, in TreePath path, in ValueHash256 keccak, ReadFlags readFlags = ReadFlags.None)
    {
        // Some of the code does not save empty tree at all so this is more about correctness than optimization.
        if (keccak == Keccak.EmptyTreeHash.ValueHash256)
        {
            return EmptyTreeHashBytes;
        }

        if (Scheme == INodeStorage.KeyScheme.HalfPath && (readFlags & ReadFlags.HintReadAhead) != 0)
        {
            if (address is null && path.Length > TopStateBoundary)
            {
                readFlags |= ReadFlags.HintReadAhead2;
            }
            else if (address is not null)
            {
                readFlags |= ReadFlags.HintReadAhead3;
            }
        }

        Span<byte> storagePathSpan = stackalloc byte[StoragePathLength];
        if (Scheme == INodeStorage.KeyScheme.HalfPath)
        {
            return _keyValueStore.Get(GetHalfPathNodeStoragePathSpan(storagePathSpan, address, path, keccak), readFlags)
                   ?? _keyValueStore.Get(GetHashBasedStoragePath(storagePathSpan, keccak), readFlags);
        }

        return _keyValueStore.Get(GetHashBasedStoragePath(storagePathSpan, keccak), readFlags)
               ?? _keyValueStore.Get(GetHalfPathNodeStoragePathSpan(storagePathSpan, address, path, keccak), readFlags);
    }

    public bool KeyExists(in ValueHash256? address, in TreePath path, in ValueHash256 keccak)
    {
        if (keccak == Keccak.EmptyTreeHash.ValueHash256)
        {
            return true;
        }

        Span<byte> storagePathSpan = stackalloc byte[StoragePathLength];
        if (Scheme == INodeStorage.KeyScheme.HalfPath)
        {
            return _keyValueStore.KeyExists(GetHalfPathNodeStoragePathSpan(storagePathSpan, address, path, keccak))
                   || _keyValueStore.KeyExists(GetHashBasedStoragePath(storagePathSpan, keccak));
        }

        return _keyValueStore.KeyExists(GetHashBasedStoragePath(storagePathSpan, keccak))
               || _keyValueStore.KeyExists(GetHalfPathNodeStoragePathSpan(storagePathSpan, address, path, keccak));
    }

    public INodeStorage.IWriteBatch StartWriteBatch()
    {
        IWriteBatch batch = _keyValueStore is IKeyValueStoreWithBatching withBatching
            ? withBatching.StartWriteBatch()
            : new InMemoryWriteBatch(_keyValueStore);

        return new WriteBatch(batch, this);
    }

    public void Set(Hash256? address, in TreePath path, in ValueHash256 keccak, ReadOnlySpan<byte> data, WriteFlags writeFlags = WriteFlags.None)
    {
        if (keccak == Keccak.EmptyTreeHash.ValueHash256)
        {
            return;
        }

        if (data.IsNull())
        {
            // Only delete half path key. DO NOT delete hash based key.
            _keyValueStore.Remove(GetHalfPathNodeStoragePathSpan(stackalloc byte[StoragePathLength], address, path, keccak));
            return;
        }

        _keyValueStore.PutSpan(GetExpectedPath(stackalloc byte[StoragePathLength], address, path, keccak), data, writeFlags);
    }

    public void Flush(bool onlyWal)
    {
        if (_keyValueStore is IDb db)
        {
            db.Flush(onlyWal);
        }
    }

    public void Compact()
    {
        if (_keyValueStore is IDb db)
        {
            db.Compact();
        }
    }

    private class WriteBatch(IWriteBatch writeBatch, NodeStorage nodeStorage) : INodeStorage.IWriteBatch
    {
        public void Dispose()
        {
            writeBatch.Dispose();
        }

        public void Set(Hash256? address, in TreePath path, in ValueHash256 keccak, ReadOnlySpan<byte> data, WriteFlags writeFlags)
        {
            if (keccak != Keccak.EmptyTreeHash.ValueHash256)
            {
                writeBatch.PutSpan(nodeStorage.GetExpectedPath(stackalloc byte[StoragePathLength], address, path, keccak), data, writeFlags);
            }
        }
    }
}
