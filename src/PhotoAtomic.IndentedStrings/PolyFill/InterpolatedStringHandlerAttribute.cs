// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Vendored via https://github.com/PhotoAtomic/PhotoAtomic.Darc — polyfill for netstandard2.0.

namespace System.Runtime.CompilerServices;

/// <summary>Indicates the attributed type is to be used as an interpolated string handler.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
internal sealed class InterpolatedStringHandlerAttribute : Attribute
{
    /// <summary>Initializes the <see cref="InterpolatedStringHandlerAttribute"/>.</summary>
    public InterpolatedStringHandlerAttribute() { }
}
