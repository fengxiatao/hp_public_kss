using Avalonia.Media;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using System;

namespace FaceLocker.ViewModels
{
    public class ViewModelBase : ReactiveObject
    {
        #region 获取应用程序配置接口
        /// <summary>
        /// 获取应用程序配置接口
        /// </summary>
        protected IConfiguration Configuration => App.Configuration;
        #endregion

        #region 获取指定键的配置值
        /// <summary>
        /// 获取指定键的配置值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值</returns>
        protected T GetAppSetting<T>(string key, T defaultValue = default(T))
        {
            try
            {
                var value = Configuration?[key];
                if (value == null)
                {
                    return defaultValue;
                }

                // 如果T是string，直接返回
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)value;
                }

                // 尝试转换类型
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }
        #endregion

        #region 获取配置节点
        /// <summary>
        /// 获取配置节点
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>配置节点</returns>
        protected IConfigurationSection GetAppSettingSection(string key)
        {
            return Configuration?.GetSection(key);
        }
        #endregion

        #region 检查配置是否存在
        /// <summary>
        /// 检查配置是否存在
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>是否存在</returns>
        protected bool AppSettingExists(string key)
        {
            return Configuration?[key] != null;
        }
        #endregion

        #region 获取设备名称
        /// <summary>
        /// 获取设备名称
        /// 优先从配置中获取，如果配置中没有则使用机器名
        /// </summary>
        /// <returns>设备名称</returns>
        protected string GetDeviceName()
        {
            // 优先从配置中获取设备名
            var configuredDeviceName = GetAppSetting<string>("Server:DeviceName");
            if (!string.IsNullOrWhiteSpace(configuredDeviceName))
            {
                return configuredDeviceName.Trim();
            }
            else
            {
                // 获取机器名
                var machineName = Environment.MachineName;
                return machineName;
            }
        }
        #endregion

        #region 获取本地网络IP地址
        /// 获取本地网络IP地址
        /// </summary>
        /// <returns>IP地址字符串</returns>
        protected string GetNetworkIP()
        {
            try
            {
                // 优先从配置中获取IP地址
                var configuredIP = GetAppSetting<string>("LockController:IPAddress");
                if (!string.IsNullOrWhiteSpace(configuredIP))
                {
                    return configuredIP.Trim();
                }

                // 最终备用方案
                return "127.0.0.1";
            }
            catch (Exception)
            {
                return "127.0.0.1";
            }
        }
        #endregion

        #region 获取柜组名称
        /// <summary>
        /// 获取柜组名称
        /// </summary>
        /// <returns>柜组名称</returns>
        protected string GetLockerGroupName()
        {
            // 从配置中获取柜组名称
            var groupName = GetAppSetting<string>("LockController:GroupName");
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                return groupName.Trim();
            }
            else
            {
                return "KSS";
            }
        }
        #endregion

        #region 获取柜子排序方向
        /// <summary>
        /// 获取柜子排序方向
        /// 0: 从左向右排序
        /// 1: 从右向左排序
        /// </summary>
        /// <returns>排序方向</returns>
        protected int GetLockerSortDirection()
        {
            try
            {
                var direction = GetAppSetting<int>("LockController:Direction", 0);
                return direction;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取柜子排序方向的FlowDirection
        /// </summary>
        /// <returns>FlowDirection枚举值</returns>
        protected FlowDirection GetLockerFlowDirection()
        {
            var direction = GetLockerSortDirection();
            var flowDirection = direction == 0 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            return flowDirection;
        }
        #endregion
    }
}