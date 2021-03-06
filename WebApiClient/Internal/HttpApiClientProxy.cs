﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace WebApiClient
{
    /// <summary>
    /// 提供HttpApiClient代理类生成
    /// 不支持泛型方法的接口
    /// 不支持ref/out参数的接口
    /// </summary>
    static class HttpApiClientProxy
    {
        /// <summary>
        /// IApiInterceptor的Intercept方法
        /// </summary>
        private static readonly MethodInfo interceptMethod = typeof(IApiInterceptor).GetMethod("Intercept");

        /// <summary>
        /// HttpApiClient的构造器
        /// </summary>
        private static readonly ConstructorInfo baseConstructor = typeof(HttpApiClient).GetConstructor(new Type[] { typeof(IApiInterceptor) });

        /// <summary>
        /// 代理类型的构造器的参数类型
        /// </summary>
        private static readonly Type[] proxyTypeCtorArgTypes = new Type[] { typeof(IApiInterceptor), typeof(MethodInfo[]) };

        /// <summary>
        /// 程序集HashCode^模块HashCode与模块创建器的缓存
        /// </summary>
        private static readonly ConcurrentDictionary<int, ModuleBuilder> hashCodeModuleBuilderCache = new ConcurrentDictionary<int, ModuleBuilder>();

        /// <summary>
        /// 接口类型与代理类型的构造器缓存
        /// </summary>
        private static readonly ConcurrentDictionary<Type, ConstructorInfo> proxyTypeCtorCache = new ConcurrentDictionary<Type, ConstructorInfo>();

        /// <summary>
        /// 创建HttpApiClient代理类
        /// 并实现指定的接口
        /// </summary>
        /// <typeparam name="TInterface">接口类型</typeparam>
        /// <param name="interceptor">拦截器</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <returns></returns>
        public static TInterface CreateProxyWithInterface<TInterface>(IApiInterceptor interceptor) where TInterface : class
        {
            var interfaceType = typeof(TInterface);
            var apiMethods = interfaceType.GetApiAllMethods(typeof(IHttpApiClient), typeof(IDisposable));
            var proxyTypeCtor = proxyTypeCtorCache.GetOrAdd(interfaceType, type => GenerateProxyTypeCtor(type, apiMethods));
            return proxyTypeCtor.Invoke(new object[] { interceptor, apiMethods }) as TInterface;
        }

        /// <summary>
        /// 生成接口的代理类
        /// 返回其构造器
        /// </summary>
        /// <param name="interfaceType">接口类型</param>
        /// <param name="apiMethods">拦截的方法</param>
        /// <returns></returns>
        private static ConstructorInfo GenerateProxyTypeCtor(Type interfaceType, MethodInfo[] apiMethods)
        {
            var moduleName = interfaceType.Module.Name;
            var hashCode = interfaceType.Assembly.GetHashCode() ^ interfaceType.Module.GetHashCode();

            // 每个动态集下面只会有一个模块
            var moduleBuilder = hashCodeModuleBuilderCache.GetOrAdd(hashCode, (hash) =>
            {
                return AssemblyBuilder
                .DefineDynamicAssembly(new AssemblyName(hash.ToString()), AssemblyBuilderAccess.Run)
                .DefineDynamicModule(moduleName);
            });

            var typeBuilder = moduleBuilder.DefineType(interfaceType.FullName, TypeAttributes.Class, typeof(HttpApiClient));
            typeBuilder.AddInterfaceImplementation(interfaceType);

            var proxyType = HttpApiClientProxy.ImplementApiMethods(typeBuilder, apiMethods);
            return proxyType.GetConstructor(proxyTypeCtorArgTypes);
        }

        /// <summary>
        /// 实现接口方法
        /// 返回代理类型
        /// </summary>
        /// <param name="typeBuilder">类型生成器</param>
        /// <param name="apiMethods">接口的所有方法</param>
        /// <returns></returns>
        private static Type ImplementApiMethods(TypeBuilder typeBuilder, MethodInfo[] apiMethods)
        {
            // 字段
            var filedAttribute = FieldAttributes.Private | FieldAttributes.InitOnly;
            var fieldInterceptor = typeBuilder.DefineField("interceptor", typeof(IApiInterceptor), filedAttribute);
            var fieldApiMethods = typeBuilder.DefineField("apiMethods", typeof(MethodInfo[]), filedAttribute);

            // 构造器         
            // this(IApiInterceptor interceptor, MethodInfo[] methods):base(interceptor)
            var ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, proxyTypeCtorArgTypes);
            var ctorIL = ctorBuilder.GetILGenerator();
            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(OpCodes.Ldarg_1);
            ctorIL.Emit(OpCodes.Call, baseConstructor);

            // this.interceptor = 第一个参数
            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(OpCodes.Ldarg_1);
            ctorIL.Emit(OpCodes.Stfld, fieldInterceptor);

            // this.apiMethods = 第二个参数
            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(OpCodes.Ldarg_2);
            ctorIL.Emit(OpCodes.Stfld, fieldApiMethods);

            ctorIL.Emit(OpCodes.Ret);

            // 接口实现
            var implementAttribute = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot | MethodAttributes.HideBySig;
            for (var i = 0; i < apiMethods.Length; i++)
            {
                var apiMethod = apiMethods[i];
                var apiParameters = apiMethod.GetParameters();
                var parameterTypes = apiParameters.Select(p => p.ParameterType).ToArray();

                var methodBuilder = typeBuilder.DefineMethod(apiMethod.Name, implementAttribute, CallingConventions.Standard, apiMethod.ReturnType, parameterTypes);
                var apiMethodIL = methodBuilder.GetILGenerator();

                // this.interceptor
                apiMethodIL.Emit(OpCodes.Ldarg_0);
                apiMethodIL.Emit(OpCodes.Ldfld, fieldInterceptor);

                // 加载target参数
                apiMethodIL.Emit(OpCodes.Ldarg_0);

                // var method = this.apiMethods[i]
                var method = apiMethodIL.DeclareLocal(typeof(MethodInfo));
                apiMethodIL.Emit(OpCodes.Ldarg_0);
                apiMethodIL.Emit(OpCodes.Ldfld, fieldApiMethods);
                apiMethodIL.Emit(OpCodes.Ldc_I4, i);
                apiMethodIL.Emit(OpCodes.Ldelem_Ref);
                apiMethodIL.Emit(OpCodes.Stloc, method);

                // 加载method参数
                apiMethodIL.Emit(OpCodes.Ldloc, method);

                // var parameters = new object[parameters.Length]
                var parameters = apiMethodIL.DeclareLocal(typeof(object[]));
                apiMethodIL.Emit(OpCodes.Ldc_I4, apiParameters.Length);
                apiMethodIL.Emit(OpCodes.Newarr, typeof(object));
                apiMethodIL.Emit(OpCodes.Stloc, parameters);

                for (var j = 0; j < apiParameters.Length; j++)
                {
                    apiMethodIL.Emit(OpCodes.Ldloc, parameters);
                    apiMethodIL.Emit(OpCodes.Ldc_I4, j);
                    apiMethodIL.Emit(OpCodes.Ldarg, j + 1);

                    var parameterType = parameterTypes[j];
                    if (parameterType.IsValueType || parameterType.IsGenericParameter)
                    {
                        apiMethodIL.Emit(OpCodes.Box, parameterType);
                    }
                    apiMethodIL.Emit(OpCodes.Stelem_Ref);
                }

                // 加载parameters参数
                apiMethodIL.Emit(OpCodes.Ldloc, parameters);

                // Intercep(this, method, parameters)
                apiMethodIL.Emit(OpCodes.Callvirt, interceptMethod);

                if (apiMethod.ReturnType == typeof(void))
                {
                    apiMethodIL.Emit(OpCodes.Pop);
                }
                apiMethodIL.Emit(OpCodes.Ret);
            }
            return typeBuilder.CreateTypeInfo();
        }
    }
}
