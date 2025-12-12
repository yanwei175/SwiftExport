using SwiftExport.AppLayer.DTOs;
using SwiftExport.Core.EntitieView;

namespace SwiftExport.UiWinForm.静态类
{
    public static class ActiveUserClass
    {
        private static ViewUserRole? _currentUser;

        public static ViewUserRole ActiveUser
        {
            get
            {
                if (_currentUser == null)
                    throw new InvalidOperationException("用户尚未登录");
                return _currentUser;
            }
            set
            {
                _currentUser = value; // 这里可以随时替换
            }
        }
    }


}
