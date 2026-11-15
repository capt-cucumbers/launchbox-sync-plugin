using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unbroken.LaunchBox.Plugins.Data;

namespace LaunchBoxSyncPlugin.Tests
{
    internal class MethodProxy<T> : DispatchProxy where T : class
    {
        private readonly Dictionary<string, object?> _properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        private Func<MethodInfo, object?[]?, object?>? _methodHandler;
        private Action<string>? _accessObserver;

        public void Configure(
            Func<MethodInfo, object?[]?, object?>? methodHandler = null,
            Action<string>? accessObserver = null)
        {
            _methodHandler = methodHandler;
            _accessObserver = accessObserver;
        }

        public void SetProperty(string name, object? value)
        {
            _properties[name] = value;
        }

        public object? GetProperty(string name)
        {
            return _properties.TryGetValue(name, out var val) ? val : null;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) return null;

            _accessObserver?.Invoke(targetMethod.Name);

            if (targetMethod.IsSpecialName && targetMethod.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                var propName = targetMethod.Name.Substring(4);
                if (_properties.TryGetValue(propName, out var val))
                {
                    return val;
                }
                return GetDefault(targetMethod.ReturnType);
            }

            if (targetMethod.IsSpecialName && targetMethod.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                var propName = targetMethod.Name.Substring(4);
                _properties[propName] = args != null && args.Length > 0 ? args[0] : null;
                return null;
            }

            if (_methodHandler != null)
            {
                return _methodHandler(targetMethod, args);
            }

            return GetDefault(targetMethod.ReturnType);
        }

        private static object? GetDefault(Type type)
        {
            if (type == typeof(void)) return null;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    internal static class LaunchBoxFakes
    {
        public static ICustomField CreateCustomField(string name = "", string value = "", string gameId = "")
        {
            var proxy = DispatchProxy.Create<ICustomField, MethodProxy<ICustomField>>();
            var bag = (MethodProxy<ICustomField>)(object)proxy;
            bag.SetProperty(nameof(ICustomField.Name), name);
            bag.SetProperty(nameof(ICustomField.Value), value);
            bag.SetProperty(nameof(ICustomField.GameId), gameId);
            return proxy;
        }

        public static IAdditionalApplication CreateAdditionalApp(string name = "")
        {
            var proxy = DispatchProxy.Create<IAdditionalApplication, MethodProxy<IAdditionalApplication>>();
            var bag = (MethodProxy<IAdditionalApplication>)(object)proxy;
            bag.SetProperty(nameof(IAdditionalApplication.Name), name);
            bag.SetProperty(nameof(IAdditionalApplication.Id), Guid.NewGuid().ToString());
            return proxy;
        }

        public static IGame CreateGame(
            string id = "",
            string title = "",
            Action<string, int>? accessThreadRecorder = null)
        {
            var gameId = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id;
            var customFields = new List<ICustomField>();
            var addlApps = new List<IAdditionalApplication>();

            var proxy = DispatchProxy.Create<IGame, MethodProxy<IGame>>();
            var bag = (MethodProxy<IGame>)(object)proxy;
            bag.SetProperty(nameof(IGame.Id), gameId);
            bag.SetProperty(nameof(IGame.Title), title);

            bag.Configure(
                methodHandler: (method, _) =>
                {
                    return method.Name switch
                    {
                        nameof(IGame.GetAllCustomFields) => customFields.ToArray(),
                        nameof(IGame.AddNewCustomField) => CreateAndAddCustomField(customFields, gameId),
                        nameof(IGame.GetAllAdditionalApplications) => addlApps.ToArray(),
                        nameof(IGame.AddNewAdditionalApplication) => CreateAndAddAddlApp(addlApps),
                        _ => method.ReturnType == typeof(void) ? null : (method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null)
                    };
                },
                accessObserver: memberName =>
                {
                    accessThreadRecorder?.Invoke($"IGame.{memberName}", Environment.CurrentManagedThreadId);
                });

            return proxy;
        }

        private static ICustomField CreateAndAddCustomField(List<ICustomField> customFields, string gameId)
        {
            var field = CreateCustomField(gameId: gameId);
            customFields.Add(field);
            return field;
        }

        private static IAdditionalApplication CreateAndAddAddlApp(List<IAdditionalApplication> addlApps)
        {
            var app = CreateAdditionalApp();
            addlApps.Add(app);
            return app;
        }
    }

    internal sealed class FakeDataManagerHarness
    {
        private readonly List<IGame> _games = new List<IGame>();
        public List<(string Member, int ThreadId)> ObservedAccesses { get; } = new List<(string Member, int ThreadId)>();
        public int SaveCount { get; private set; }
        public int AddNewGameCount { get; private set; }
        public IDataManager Instance { get; }

        public FakeDataManagerHarness(IEnumerable<IGame>? initialGames = null)
        {
            if (initialGames != null)
            {
                _games.AddRange(initialGames);
            }

            var proxy = DispatchProxy.Create<IDataManager, MethodProxy<IDataManager>>();
            var bag = (MethodProxy<IDataManager>)(object)proxy;
            bag.Configure(
                methodHandler: (method, args) =>
                {
                    switch (method.Name)
                    {
                        case nameof(IDataManager.GetAllGames):
                            return _games.ToArray();
                        case nameof(IDataManager.AddNewGame):
                            AddNewGameCount++;
                            var title = args != null && args.Length > 0 ? args[0] as string ?? string.Empty : string.Empty;
                            var created = LaunchBoxFakes.CreateGame(
                                title: title,
                                accessThreadRecorder: (m, tid) => ObservedAccesses.Add((m, tid)));
                            _games.Add(created);
                            return created;
                        case nameof(IDataManager.GetGameById):
                            var id = args != null && args.Length > 0 ? args[0] as string : null;
                            return _games.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                        case nameof(IDataManager.Save):
                            SaveCount++;
                            return null;
                        default:
                            return method.ReturnType == typeof(void)
                                ? null
                                : (method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null);
                    }
                },
                accessObserver: memberName =>
                {
                    ObservedAccesses.Add(($"IDataManager.{memberName}", Environment.CurrentManagedThreadId));
                });

            Instance = proxy;
        }

        public IReadOnlyList<IGame> Games => _games;
    }
}
