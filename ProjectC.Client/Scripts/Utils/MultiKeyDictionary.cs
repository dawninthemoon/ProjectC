using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class MultiKeyDictionary<Key1, Key2, V> : Dictionary<Key1, Dictionary<Key2, V>>
{
    public V this[Key1 key1, Key2 key2]
    {
        get
        {
            if (!ContainsKey(key1) || !this[key1].ContainsKey(key2))
            {
                throw new ArgumentOutOfRangeException();
            }
            return base[key1][key2];
        }
        set
        { 
            if (!ContainsKey(key1))
            {
                this[key1] = new Dictionary<Key2, V>();
            }
            this[key1][key2] = value;
        }
    }

    public bool TryGetValue(Key1 key1, Key2 key2, out V value)
    {
        Dictionary<Key2, V> output1;
        V output2;
        if (!TryGetValue(key1, out output1) || !output1.TryGetValue(key2, out output2))
        {
            value = default(V);
            return false;
        }
        value = output2;
        return true;
    }

    public void Add(Key1 key1, Key2 key2, V value)
    {
        if (!ContainsKey(key1))
            this[key1] = new Dictionary<Key2, V>();
        this[key1][key2] = value;
    }

    public bool ContainsKey(Key1 key1, Key2 key2)
    {
        return base.ContainsKey(key1) && this[key1].ContainsKey(key2);
    }

    public new IEnumerable<V> Values
    {
        get
        {
            return from baseDict in base.Values
                   from baseKey in baseDict.Keys
                   select baseDict[baseKey];
        }
    }
}