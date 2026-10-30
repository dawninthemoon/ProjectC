using UnityEngine;
using Game.Utils;

public class Component_Base : MonoBehaviour
{
    private bool _isPreInitialized;

    public void PreInitialize()
    {
        if (_isPreInitialized)
        {
            return;
        }

        _isPreInitialized = true;
        OnPreInitialized();
    }

    protected virtual void OnPreInitialized()
    {
    }

    protected bool Bind<T>(string objectName, out T component)
        where T : Component
    {
        return UnityBindingExtensions.Bind(this, objectName, out component);
    }

    protected bool Bind(string objectName, out GameObject target)
    {
        return UnityBindingExtensions.Bind(this, objectName, out target);
    }

    protected bool SetText(string objectName, string lmk)
    {
        return UnityBindingExtensions.SetText(this, objectName, lmk);
    }

    protected bool SetTextSimple(string objectName, string text)
    {
        return UnityBindingExtensions.SetTextSimple(this, objectName, text);
    }

    protected bool BindNoRecursive<T>(out T component)
        where T : Component
    {
        return UnityBindingExtensions.BindNoRecursive(this, out component);
    }
}
