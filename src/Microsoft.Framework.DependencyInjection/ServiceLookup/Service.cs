// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Microsoft.Framework.DependencyInjection.ServiceLookup
{
    internal class Service : IService
    {
        private readonly ServiceDescriptor _descriptor;

        public Service(ServiceDescriptor descriptor)
        {
            _descriptor = descriptor;
        }

        public IService Next { get; set; }

        public ServiceLifetime Lifetime
        {
            get { return _descriptor.Lifetime; }
        }

        public IServiceCallSite CreateCallSite(ServiceProvider provider, ISet<Type> callSiteChain)
        {
            ConstructorInfo[] constructors = _descriptor.ImplementationType.GetTypeInfo()
                .DeclaredConstructors
                .Where(IsInjectable)
                .ToArray();

            // TODO: actual service-fulfillment constructor selection
            if (constructors.Length == 1)
            {
                ParameterInfo[] parameters = constructors[0].GetParameters();
                IServiceCallSite[] parameterCallSites = new IServiceCallSite[parameters.Length];
                for (var index = 0; index != parameters.Length; ++index)
                {
                    parameterCallSites[index] = provider.GetServiceCallSite(parameters[index].ParameterType, callSiteChain);
                
                    if (parameterCallSites[index] == null && parameters[index].HasDefaultValue)
                    {
                        parameterCallSites[index] = new ConstantCallSite(parameters[index].DefaultValue);
                    }
                    if (parameterCallSites[index] == null)
                    {
                        throw new InvalidOperationException(Resources.FormatCannotResolveService(
                                parameters[index].ParameterType, 
                                _descriptor.ImplementationType));
                    }
                }
                return new ConstructorCallSite(constructors[0], parameterCallSites);
            }

            return new CreateInstanceCallSite(_descriptor);
        }

        private static bool IsInjectable(ConstructorInfo constructor)
        {
            return constructor.IsPublic && constructor.GetParameters().Length != 0;
        }

        private class ConstantCallSite : IServiceCallSite
        {
            private readonly object _defaultValue;

            public ConstantCallSite(object defaultValue)
            {
                _defaultValue = defaultValue;
            }

            public object Invoke(ServiceProvider provider)
            {
                return _defaultValue;
            }

            public Expression Build(Expression provider)
            {
                return Expression.Constant(_defaultValue);
            }
        }

        private class ConstructorCallSite : IServiceCallSite
        {
            private readonly ConstructorInfo _constructorInfo;
            private readonly IServiceCallSite[] _parameterCallSites;

            public ConstructorCallSite(ConstructorInfo constructorInfo, IServiceCallSite[] parameterCallSites)
            {
                _constructorInfo = constructorInfo;
                _parameterCallSites = parameterCallSites;
            }

            public object Invoke(ServiceProvider provider)
            {
                object[] parameterValues = new object[_parameterCallSites.Length];
                for (var index = 0; index != parameterValues.Length; ++index)
                {
                    parameterValues[index] = _parameterCallSites[index].Invoke(provider);
                }

                try
                {
                    return _constructorInfo.Invoke(parameterValues);
                }
                catch (Exception ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    // The above line will always throw, but the compiler requires we throw explicitly.
                    throw;
                }
            }

            public Expression Build(Expression provider)
            {
                var parameters = _constructorInfo.GetParameters();
                return Expression.New(
                    _constructorInfo,
                    _parameterCallSites.Select((callSite, index) =>
                        Expression.Convert(
                            callSite.Build(provider),
                            parameters[index].ParameterType)));
            }
        }

        private class CreateInstanceCallSite : IServiceCallSite
        {
            private readonly ServiceDescriptor _descriptor;

            public CreateInstanceCallSite(ServiceDescriptor descriptor)
            {
                _descriptor = descriptor;
            }

            public object Invoke(ServiceProvider provider)
            {
                try
                {
                    return Activator.CreateInstance(_descriptor.ImplementationType);
                }
                catch (Exception ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    // The above line will always throw, but the compiler requires we throw explicitly.
                    throw;
                }
            }

            public Expression Build(Expression provider)
            {
                return Expression.New(_descriptor.ImplementationType);
            }
        }
    }
}
