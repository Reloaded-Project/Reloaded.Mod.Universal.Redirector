﻿using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Reloaded.Universal.Redirector.Lib.Extensions;
using Reloaded.Universal.Redirector.Lib.Utility;

namespace Reloaded.Universal.Redirector.Lib.Structures;

/// <summary>
/// Provides a limited dictionary implementation with the following characteristics:
///     - String Key, Also Indexable with Span{char}.
///     - Non-threadsafe.
/// </summary>
public class SpanOfCharDict<T>
{
    // Note: Do not need a Remove function, for our purposes, we'll never end up using it,
    // because we will need a full rebuild on file removal at runtime.
    
    private DictionaryEntry[] _entries; // buffer of entries. Placed together for improved cache coherency.
    private int[] _buckets; // pointers to first entry in each bucket. Encoded as 1 based, so default 0 value is seen as invalid.
    
    /// <summary>
    /// Number of items stored in this dictionary.
    /// </summary>
    public int Count { get; private set; } // also index of next entry
    
    /// <summary/>
    /// <param name="targetSize">Amount of expected items in this dictionary.</param>
    public SpanOfCharDict(int targetSize)
    {
        // Min size.
        if (targetSize <= 0)
            targetSize = 8;
        
        // Round up to next power of 2
        _buckets = new int[BitOperations.RoundUpToPowerOf2((uint)(targetSize))];
        _entries = new DictionaryEntry[targetSize];
    }
    
    /// <summary>
    /// Clones this dictionary instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOfCharDict<T> Clone()
    {
        var result = new SpanOfCharDict<T>(Count);
        
        // Copy existing items.
        // Inlined: GetValues for perf. Notably because hot path; so memory saving here might be not so bad.
        int x = 0;
        int count = Count;
        while (x < count)
        {
            x = GetNextItemIndex(x);
            if (x == -1) 
                return result;

            ref var entry = ref _entries.DangerousGetReferenceAt(x++);
            result.AddOrReplaceWithKnownHashCode(entry.HashCode, entry.Key!, entry.Value);
        }

        return result;
    }

    /// <summary>
    /// Adds or replaces a specified value in the dictionary.
    /// </summary>
    /// <param name="key">The key for the dictionary.</param>
    /// <param name="value">The value to be inserted into the dictionary.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddOrReplace(string key, T value)
    {
        AddOrReplaceWithKnownHashCode(key.AsSpan().GetNonRandomizedHashCode(), key, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddOrReplaceWithKnownHashCode(nuint hashCode, string key, T value)
    {
        // Grow if needed.
        var count = Count;
        if (count >= _entries.Length)
            GrowDictionaryRare();
        
        ref var entryIndex = ref GetBucketEntry(hashCode);

        // No entry exists for this bucket.
        if (entryIndex <= 0)
        {
            entryIndex = count + 1; // Bucket entries encoded as 1 indexed.

            ref DictionaryEntry newEntry = ref _entries.DangerousGetReferenceAt(count);
            newEntry.Key = key;
            newEntry.Value = value;
            newEntry.HashCode = hashCode;
            // newEntry.NextItem = 0; <= not needed, since we don't support removal and will already be initialised as 0.
            Count = count + 1;
            return;
        }

        // Get entry.
        var keySpan = key.AsSpan();
        var index = entryIndex - 1;
        ref DictionaryEntry entry = ref Unsafe.NullRef<DictionaryEntry>();

        do
        {
            entry = ref _entries.DangerousGetReferenceAt(index);
            if (entry.HashCode == hashCode && keySpan.SequenceEqual(entry.Key.AsSpan()))
            {
                // Update existing entry.
                entry.Value = value;
                return;
            }

            index = (int)(entry.NextItem - 1);
        } while (index > 0);

        // Item is not in there, we add and exit.
        ref DictionaryEntry nextEntry = ref _entries.DangerousGetReferenceAt(count);
        nextEntry.Key = key;
        nextEntry.Value = value;
        nextEntry.HashCode = hashCode;
        // entry.NextItem = 0; <= not needed, since we don't support removal and will already be initialised as 0.

        // Update last in chain and total count.
        entry.NextItem = (uint)count + 1;
        Count = count + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // On a hot path but rarely call, do not inline.
    private void GrowDictionaryRare()
    {
        var newEntries = new DictionaryEntry[_entries.Length * 2];
        _entries.AsSpan().CopyTo(newEntries);
        _entries = newEntries;
    }

    /// <summary>
    /// Checks if a given item is present in the dictionary.
    /// </summary>
    /// <param name="key">The key for the dictionary.</param>
    /// <returns>True if the item was found, else false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(ReadOnlySpan<char> key) => TryGetValue(key, out _);

    /// <summary>
    /// Gets an item from the dictionary if present.
    /// </summary>
    /// <param name="key">The key for the dictionary.</param>
    /// <param name="value">The value to return.</param>
    /// <returns>True if the item was found, else false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(ReadOnlySpan<char> key, [MaybeNullWhen(false)] out T value)
    {
        value = default;
        var hashCode   = key.GetNonRandomizedHashCode();
        var entryIndex = GetBucketEntry(hashCode);

        // No entry exists for this bucket.
        // Note: Do not invert branch. We assume it is not taken in ASM.
        // It is written this way as entryindex <= 0 is the rare(r) case.
        if (entryIndex > 0)
        {
            var index = entryIndex - 1; // Move up here because 3 instructions below [DangerousGetReferenceAt] depends on this.
            ref DictionaryEntry entry = ref Unsafe.NullRef<DictionaryEntry>();
            var entries = _entries;

            do
            {
                entry = ref entries.DangerousGetReferenceAt(index);
                if (entry.HashCode == hashCode && key.SequenceEqual(entry.Key.AsSpan()))
                {
                    value = entry.Value;
                    return true;
                }

                index = (int)(entry.NextItem - 1);
            } while (index > 0);

            return false;
        }

        return false;
    }

    /// <summary>
    /// Gets an item from the dictionary if present, by reference.
    /// </summary>
    /// <param name="key">The key for the dictionary.</param>
    /// <returns>
    ///    Null reference if not found, else a valid reference.
    ///    Use <see cref="Unsafe.IsNullRef{T}"/> to test.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public ref T GetValueRef(ReadOnlySpan<char> key)
    {
        // without aggressive inline, ~1% faster than runtime when string, ~1% slower on ROS<char>
        // with inline, ~15% faster.
        var hashCode   = key.GetNonRandomizedHashCode();
        var entryIndex = GetBucketEntry(hashCode);

        // No entry exists for this bucket.
        // Note: Do not invert branch. We assume it is not taken in ASM.
        // It is written this way as entryindex <= 0 is the rare(r) case.
        if (entryIndex > 0)
        {
            var index = entryIndex - 1; // Move up here because 3 instructions below [DangerousGetReferenceAt] depends on this.
            ref DictionaryEntry entry = ref Unsafe.NullRef<DictionaryEntry>();
            var entries = _entries;

            do
            {
                entry = ref entries.DangerousGetReferenceAt(index);
                if (entry.HashCode == hashCode && key.SequenceEqual(entry.Key))
                    return ref entry.Value;

                index = (int)(entry.NextItem - 1);
            } while (index > 0);

            return ref Unsafe.NullRef<T>();
        }

        return ref Unsafe.NullRef<T>();
    }

    /// <summary>
    /// An optimised search implementation that returns the first value in dictionary by reference.
    /// </summary>
    /// <param name="key">The key of this item.</param>
    /// <remarks>
    ///     This is intended to be used when <see cref="Count"/> == 1.
    ///     When this is not the case, element returned is undefined.
    /// </remarks>
    /// <returns>
    ///    Null reference if not found, else a valid reference.
    ///    Use <see cref="Unsafe.IsNullRef{T}"/> to test.
    /// </returns>
    public ref T GetFirstItem(out string? key)
    {
        int index = GetNextItemIndex(0);
        if (index != -1)
        {
            ref var entry = ref _entries.DangerousGetReferenceAt(index);
            key = entry.Key;
            return ref entry.Value;
        }

        key = default;
        return ref Unsafe.NullRef<T>();
    }

    /// <summary>
    /// Gets an enumerator that exposes all values available in this dictionary instance.
    /// </summary>
    public EntryEnumerator GetEntryEnumerator()
    {
        // Note: Significant performance Ws from this.
        return new EntryEnumerator(this);
    }

    /// <inheritdoc />
    public struct EntryEnumerator : IEnumerator<DictionaryEntry>
    {
        private int CurrentIndex { get; set; }
        private SpanOfCharDict<T> Owner { get; }

        /// <inheritdoc />
        public DictionaryEntry Current { get; private set; }

        /// <summary/>
        /// <param name="owner">The dictionary that owns this enumerator.</param>
        public EntryEnumerator(SpanOfCharDict<T> owner) => Owner = owner;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (CurrentIndex < Owner.Count)
            {
                CurrentIndex = Owner.GetNextItemIndex(CurrentIndex);
                
                // Hot path is no branch, hence written this way
                if (CurrentIndex != -1)
                {
                    Current = Owner._entries.DangerousGetReferenceAt(CurrentIndex++);
                    return true;
                }
                
                return false;
            }
            
            return false;
        }

        /// <inheritdoc />
        public void Reset() => CurrentIndex = 0;

        /// <inheritdoc />
        public void Dispose() { }
        object IEnumerator.Current => Current;
    }

    private int GetNextItemIndex(int x)
    {
        const int unrollFactor = 4; // for readability purposes
        int maxItem = Math.Max(Count - unrollFactor, 0);
        for (; x < maxItem; x += unrollFactor)
        {
            ref var x0 = ref _entries.DangerousGetReferenceAt(x);
            ref var x1 = ref _entries.DangerousGetReferenceAt(x + 1);
            ref var x2 = ref _entries.DangerousGetReferenceAt(x + 2);
            ref var x3 = ref _entries.DangerousGetReferenceAt(x + 3);

            // Remember, we are 1 indexed
            if (x0.Key != null)
                return x;

            if (x1.Key != null)
                return x + 1;

            if (x2.Key != null)
                return x + 2;

            if (x3.Key != null)
                return x + 3;
        }

        // Not-unroll remainder
        int count = Count;
        for (; x < count; x++)
        {
            ref var x0 = ref _entries.DangerousGetReferenceAt(x);
            if (x0.Key != null)
                return x;
        }

        return -1;
    }


    /// <summary>
    /// Gets index of first entry from bucket.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetBucketEntry(nuint hashCode)
    {
        return ref _buckets.DangerousGetReferenceAt((int)hashCode & (_buckets.Length - 1));
    }
    
    /// <summary>
    /// Individual dictionary entry in the dictionary.
    /// </summary>
    public struct DictionaryEntry
    {
        /// <summary>
        /// Index of next item. 1 indexed.
        /// </summary>
        public uint NextItem;
        
        /// <summary>
        /// Full hashcode for this item.
        /// </summary>
        public nuint HashCode;
        
        /// <summary>
        /// Key for this item.
        /// </summary>
        public string? Key;
        
        /// <summary>
        /// Value for this item.
        /// </summary>
        public T Value;
    }
}     