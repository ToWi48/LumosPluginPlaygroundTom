using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumos.GUI.Executor;
using Lumos.GUI.Facade.Executor;
using org.dmxc.lumos.Kernel.Executor;

namespace LumosPluginShared
{
    /// <summary>
    /// READ-ONLY access to the executor data the DMXControl GUI already has cached.
    ///
    /// Never use IExecutorPageFacade.GUIExecutors / GetExecutorByIDAsync / GetExecutorMemberWrapperFacadeByID from a
    /// plugin: they load missing facades from the kernel into the GUI's facade cache without raising
    /// ExecutorManager.ExecutorAdded. When that happens while a project loads, ExecutorManager.refreshContent()
    /// thinks the executors are already known, never raises ExecutorAdded, and the executor window stays empty
    /// ("No Executors available").
    /// </summary>
    internal static class ExecutorCache
    {
        private static PropertyInfo _pageDescriptorProp;
        private static PropertyInfo _executorIdsProp;

        /// <summary>
        /// Executors of a page in page order. Executors the GUI has not cached yet are returned as null
        /// (keeps the positions stable); they appear as soon as DMXControl has loaded them itself.
        /// </summary>
        public static List<IExecutorFacade> GetExecutors(IExecutorPageFacade page)
        {
            var result = new List<IExecutorFacade>();
            IEnumerable<string> ids = GetExecutorIds(page);
            if (ids == null)
            {
                return result;
            }
            IReadOnlyDictionary<string, IExecutorFacade> cache = ExecutorManager.getInstance().ExecutorsDictionary;
            foreach (string id in ids)
            {
                result.Add(id != null && cache.TryGetValue(id, out IExecutorFacade ex) ? ex : null);
            }
            return result;
        }

        /// <summary>Member (cuelist, macro, master, ...) of an executor, from the cache only; null if not (yet) known.</summary>
        public static IGUIExecutorMember GetMember(IExecutorFacade executor)
        {
            string wrapperId = executor?.ExecutorMemberID;
            if (string.IsNullOrEmpty(wrapperId))
            {
                return null;
            }
            IEnumerable<IExecutorMemberWrapperFacade> wrappers = ExecutorManager.getInstance().ExecutorMemberWrapperFacades;
            IExecutorMemberWrapperFacade wrapper = null;
            if (wrappers is IReadOnlyDictionary<string, IExecutorMemberWrapperFacade> dict)
            {
                dict.TryGetValue(wrapperId, out wrapper);
            }
            else
            {
                wrapper = wrappers?.FirstOrDefault(w => w?.ID == wrapperId);
            }
            try
            {
                // reads ExecutorManager.ExecutorMembers (cached cuelists / macros / masters / timecodes)
                return wrapper?.ExecutorMember;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> GetExecutorIds(IExecutorPageFacade page)
        {
            if (page == null)
            {
                return null;
            }
            try
            {
                // ExecutorPageFacade.ExecutorPage (private) = cached descriptor, no kernel call
                if (_pageDescriptorProp == null || !_pageDescriptorProp.DeclaringType.IsInstanceOfType(page))
                {
                    _pageDescriptorProp = page.GetType().GetProperty("ExecutorPage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                object descriptor = _pageDescriptorProp?.GetValue(page);
                if (descriptor == null)
                {
                    return null;
                }
                if (_executorIdsProp == null || !_executorIdsProp.DeclaringType.IsInstanceOfType(descriptor))
                {
                    _executorIdsProp = descriptor.GetType().GetProperty("ExecutorIds");
                }
                return (_executorIdsProp?.GetValue(descriptor) as IEnumerable)?.Cast<object>().Select(o => o?.ToString()).ToList();
            }
            catch
            {
                return null;
            }
        }
    }
}
