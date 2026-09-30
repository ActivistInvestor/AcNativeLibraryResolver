

using AcMgdLib.Diagnostics;

/// DisposeAction.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license
/// 
/// This file contains code excerpted from AcMgdLib.
namespace AcMgdLib.Runtime
{
   /// <summary>
   /// Stores a value passed to the constructor, and passes that 
   /// value to the supplied delegate when the instance is disposed.
   /// </summary>
   /// <typeparam name="T">The type of the managed value.</typeparam>

   public class DisposeAction<T> : IDisposable
   {
      private readonly T value;
      private Action<T> disposeAction;
      private bool disposed;

      /// <summary>
      /// Creates an instance of <see cref="DisposeAction{T}"/>.
      /// </summary>
      /// <param name="value">The value passed to the <paramref name="disposeAction"/> delegate upon disposal.</param>
      /// <param name="disposeAction">The delegate executed upon disposal.</param>
      public DisposeAction(T value, Action<T> disposeAction)
      {
         Assert.IsNotNull(disposeAction);
         this.value = value;
         this.disposeAction = disposeAction;
      }

      public virtual T Value => value;

      protected virtual void Restore()
      {
         disposeAction?.Invoke(value);
      }

      public void Dispose()
      {
         if(!disposed)
         {
            try
            {
               Restore();
            }
            finally
            {
               disposed = true;
               GC.SuppressFinalize(this);
            }
         }
      }

      public static implicit operator T(DisposeAction<T> operand)
          => operand != null ? operand.Value : throw new ArgumentNullException(nameof(operand));
   }

}