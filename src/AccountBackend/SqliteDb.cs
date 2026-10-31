using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace AccountBackend;

public sealed class SqliteDb : IDisposable
{
    const string Library="account_sqlite";
    IntPtr handle;
    static SqliteDb() { NativeLibrary.SetDllImportResolver(typeof(SqliteDb).Assembly,(name,_,_)=>name==Library?NativeLibrary.Load(OperatingSystem.IsWindows()?"winsqlite3.dll":OperatingSystem.IsMacOS()?"libsqlite3.dylib":"libsqlite3.so.0"):IntPtr.Zero); }
    public SqliteDb(string path) { Check(sqlite3_open_v2(path,out handle,2|4|65536,IntPtr.Zero)); Check(sqlite3_busy_timeout(handle,10000)); }
    void Check(int result) { if(result!=0) throw new InvalidOperationException("SQLite: "+Marshal.PtrToStringUTF8(sqlite3_errmsg(handle))); }
    public int Exec(string sql,params object?[] args) {
        if(args.Length==0) { var code=sqlite3_exec(handle,sql,IntPtr.Zero,IntPtr.Zero,out var error); if(error!=IntPtr.Zero) sqlite3_free(error); Check(code); }
        else Query(sql,args);
        return sqlite3_changes(handle);
    }
    public List<JObject> Query(string sql,params object?[] args) {
        Check(sqlite3_prepare_v2(handle,sql,-1,out var statement,IntPtr.Zero));
        try {
            for(int i=0;i<args.Length;i++) {
                var value=args[i]; var code=value switch { null=>sqlite3_bind_null(statement,i+1),float or double or decimal=>sqlite3_bind_double(statement,i+1,Convert.ToDouble(value)),string text=>sqlite3_bind_text(statement,i+1,text,-1,new IntPtr(-1)),_=>sqlite3_bind_int64(statement,i+1,Convert.ToInt64(value)) }; Check(code);
            }
            var rows=new List<JObject>(); int result;
            while((result=sqlite3_step(statement))==100) {
                var row=new JObject(); for(int i=0;i<sqlite3_column_count(statement);i++) {
                    var name=Marshal.PtrToStringUTF8(sqlite3_column_name(statement,i))!;
                    row[name]=sqlite3_column_type(statement,i) switch { 1=>new JValue(sqlite3_column_int64(statement,i)),2=>new JValue(sqlite3_column_double(statement,i)),5=>JValue.CreateNull(),_=>new JValue(Marshal.PtrToStringUTF8(sqlite3_column_text(statement,i))) };
                } rows.Add(row);
            }
            if(result!=101) Check(result); return rows;
        } finally { sqlite3_finalize(statement); }
    }
    public void Dispose() { if(handle!=IntPtr.Zero) { sqlite3_close_v2(handle); handle=IntPtr.Zero; } }
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int ms);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr arg,out IntPtr error);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sqlite3_free(IntPtr value);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int bytes,out IntPtr statement,IntPtr tail);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_bind_null(IntPtr statement,int index);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_bind_int64(IntPtr statement,int index,long value);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_bind_double(IntPtr statement,int index,double value);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_bind_text(IntPtr statement,int index,[MarshalAs(UnmanagedType.LPUTF8Str)]string value,int bytes,IntPtr destroy);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr statement);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_name(IntPtr statement,int index);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_type(IntPtr statement,int index);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern long sqlite3_column_int64(IntPtr statement,int index);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern double sqlite3_column_double(IntPtr statement,int index);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_changes(IntPtr db);
}
