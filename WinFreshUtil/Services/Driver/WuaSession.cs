using System;
using System.Globalization;
using System.Reflection;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The update agent of Windows, which is a program that is called through COM.
    ///
    /// The calls are made through the late binding of COM, so that no library of the
    /// agent has to be beside the application. The names of the calls and the names of
    /// the values are the ones of the agent.
    /// </summary>
    public static class WuaSession
    {
        /// <summary>
        /// Starts a session with the update agent of Windows.
        /// </summary>
        public static object CreateSession()
        {
            return CreateComObject("Microsoft.Update.Session");
        }

        /// <summary>
        /// The list that holds the updates of a search or of an installation.
        /// </summary>
        public static object CreateCollection()
        {
            return CreateComObject("Microsoft.Update.UpdateColl");
        }

        /// <summary>
        /// Calls a method of the agent.
        /// </summary>
        public static object Call(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(
                name, BindingFlags.InvokeMethod, null, target, args, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a value of the agent.
        /// </summary>
        public static object GetProperty(object target, string name)
        {
            return target.GetType().InvokeMember(
                name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a value that has an index, such as the update with a number in a list.
        /// </summary>
        public static object GetIndexed(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(
                name, BindingFlags.GetProperty, null, target, args, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Writes a value of the agent.
        /// </summary>
        public static void SetProperty(object target, string name, object value)
        {
            target.GetType().InvokeMember(
                name, BindingFlags.SetProperty, null, target, new object[] { value }, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a value of the agent as a text.
        /// </summary>
        public static string GetText(object target, string name)
        {
            return AsString(GetProperty(target, name));
        }

        /// <summary>
        /// Turns a value of the agent into a text.
        /// </summary>
        public static string AsString(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static object CreateComObject(string progId)
        {
            try
            {
                Type type = Type.GetTypeFromProgID(progId);
                return type == null ? null : Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                LogService.Error("The program " + progId + " could not be started.", ex);
                return null;
            }
        }
    }
}
