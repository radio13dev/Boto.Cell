using System;
using System.Diagnostics.Contracts;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace cell.game
{
    public unsafe struct Collection<T, F> : IDisposable where T : unmanaged where F : unmanaged
    {
        public NativeArray<T> Data;

        public Collection(int length, Allocator allocator)
        {
            Data = new NativeArray<T>(length, allocator);
        }

        public T* this[Id<F> id] => (T*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(Data) + id.IndexValue;
            
        public unsafe struct Iterator
        {
            public Collection<T, F>* Collection;
            public Id<F> Index;
                
            [Pure]
            public static implicit operator bool(Iterator it) => it.Index.IndexValue >= 0 && it.Index.IndexValue < it.Collection->Data.Length;
                
            [Pure]
            public T* Value => (*Collection)[Index];
            
            public static Iterator operator++(Iterator it) => new Iterator(){ Collection = it.Collection, Index = it.Index+1 };
            
            public static bool operator ==(Iterator left, Iterator right)
            {
                return left.Index == right.Index;
            }

            public static bool operator !=(Iterator left, Iterator right)
            {
                return left.Index != right.Index;
            }
            
        }

        [Pure]
        public Iterator Iterate
        {
            get
            {
                fixed(Collection<T, F>* ptr = &this)
                    return new Iterator(){ Collection = ptr };
            }
        }

        public void Dispose()
        {
            Data.Dispose();
        }
    }

    public struct Id<T> : IEquatable<Id<T>> where T : unmanaged
    {
        public static readonly Id<T> Null = default;
        public byte Value;
        public int IndexValue => Value - 1;

        public static Id<T> Index(int v)
        {
            return new Id<T>((byte)(v + 1));
        }

        Id(byte v)
        {
            Value = v;
        }
            
        public static implicit operator bool (Id<T> id) => id != Id<T>.Null;
        public static Id<T> operator++(Id<T> id) => new Id<T>((byte)(id.Value+1));
        public static Id<T> operator+(Id<T> id, int v) => new Id<T>((byte)(id.Value+v));

        public bool Equals(Id<T> other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is Id<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public static bool operator ==(Id<T> left, Id<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Id<T> left, Id<T> right)
        {
            return !left.Equals(right);
        }
    }
}