using Argent.Models.Forms.Components.Base;
using System;
using System.Collections.Generic;

namespace Argent.Contracts.Forms
{
    public interface IFormComponentRegistry
    {
        /// <summary>
        /// Resolves the C# Type for a given JSON type string.
        /// </summary>
        Type Resolve(string typeName);

        /// <summary>
        /// Returns all registered metadata for the Form Builder toolbox.
        /// </summary>
        IEnumerable<string> GetRegisteredTypes();
    }
}
