﻿/*
 * Copyright (C) 2009, Google Inc.
 * Copyright (C) 2009, Henon <meinrad.recheis@gmail.com>
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or
 * without modification, are permitted provided that the following
 * conditions are met:
 *
 * - Redistributions of source code must retain the above copyright
 *   notice, this list of conditions and the following disclaimer.
 *
 * - Redistributions in binary form must reproduce the above
 *   copyright notice, this list of conditions and the following
 *   disclaimer in the documentation and/or other materials provided
 *   with the distribution.
 *
 * - Neither the name of the Git Development Community nor the
 *   names of its contributors may be used to endorse or promote
 *   products derived from this software without specific prior
 *   written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using GitSharp.Util;

namespace GitSharp
{
	/// <summary>
	/// Abstraction of arbitrary object storage.
	/// <para />
	/// An object database stores one or more Git objects, indexed by their unique
	/// <see cref="ObjectId"/>. Optionally an object database can reference one or more
	/// alternates; other <see cref="ObjectDatabase"/> instances that are searched in
	/// addition to the current database.
	/// <para />
	/// Databases are usually divided into two halves: a half that is considered to
	/// be fast to search, and a half that is considered to be slow to search. When
	/// alternates are present the fast half is fully searched (recursively through
	/// all alternates) before the slow half is considered.
	/// </summary>
	public abstract class ObjectDatabase
	{
		/// <summary>
		/// Constant indicating no alternate databases exist.
		/// </summary>
		protected static readonly ObjectDatabase[] NoAlternates = { };

		private readonly AtomicReference<ObjectDatabase[]> _alternates;

		/// <summary>
		/// Initialize a new database instance for access.
		/// </summary>
		protected ObjectDatabase()
		{
			_alternates = new AtomicReference<ObjectDatabase[]>();
		}

		/// <summary>
		/// Gets if this database is already created; If it returns false, the caller
		/// should invoke <see cref="Create"/> to create this database location.
		/// </summary>
		/// <returns></returns>
		public virtual bool Exists()
		{
			return true;
		}

		/// <summary>
		/// Initialize a new object database at this location.
		/// </summary>
		public virtual void Create()
		{
			// Assume no action is required.
		}

		/// <summary>
		/// Close any resources held by this database and its active alternates.
		/// </summary>
		public void Close()
		{
			CloseSelf();
			CloseAlternates();
		}

		/// <summary>
		/// Close any resources held by this database only; ignoring alternates.
		/// <para />
		/// To fully close this database and its referenced alternates, the caller
		/// should instead invoke <see cref="Close"/>.
		/// </summary>
		public virtual void CloseSelf()
		{
			// Assume no action is required.
		}

		/// <summary>
		/// Fully close all loaded alternates and clear the alternate list.
		/// </summary>
		public virtual void CloseAlternates()
		{
			ObjectDatabase[] alt = _alternates.get();
			if (alt != null)
			{
				_alternates.set(null);
				CloseAlternates(alt);
			}
		}

		/// <summary>
		/// Does the requested object exist in this database?
		/// <para />
		/// Alternates (if present) are searched automatically.
		/// </summary>
		/// <param name="objectId">identity of the object to test for existence of.</param>
		/// <returns>
		/// True if the specified object is stored in this database, or any
		/// of the alternate databases.
		/// </returns>
		public bool HasObject(AnyObjectId objectId)
		{
			return HasObjectImpl1(objectId) || HasObjectImpl2(objectId.ToString());
		}

		private bool HasObjectImpl1(AnyObjectId objectId)
		{
			if (HasObject1(objectId)) return true;

			foreach (ObjectDatabase alt in GetAlternates())
			{
				if (alt.HasObjectImpl1(objectId))
				{
					return true;
				}
			}

			return TryAgain1() && HasObject1(objectId);
		}

		private bool HasObjectImpl2(string objectId)
		{
			if (HasObject2(objectId))
			{
				return true;
			}
			foreach (ObjectDatabase alt in GetAlternates())
			{
				if (alt.HasObjectImpl2(objectId))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Fast half of <see cref="HasObject"/>.
		/// </summary>
		/// <param name="objectId">
		/// Identity of the object to test for existence of.
		/// </param>
		/// <returns>
		/// true if the specified object is stored in this database.
		/// </returns>
		protected internal abstract bool HasObject1(AnyObjectId objectId);

		/// <summary>
		/// Slow half of <see cref="HasObject"/>.
		/// </summary>
		/// <param name="objectName">
		/// Identity of the object to test for existence of.
		/// </param>
		/// <returns>
		/// true if the specified object is stored in this database.
		/// </returns>
		protected internal virtual bool HasObject2(string objectName)
		{
			// Assume the search took place during HasObject1.
			return false;
		}

		/// <summary>
		/// Open an object from this database.
		/// <para />
		/// Alternates (if present) are searched automatically.
		/// </summary>
		/// <param name="windowCursor">
		/// Temporary working space associated with the calling thread.
		/// </param>
		/// <param name="objectId">Identity of the object to open.</param>
		/// <returns>
		/// A <see cref="ObjectLoader"/> for accessing the data of the named
		/// object, or null if the object does not exist.
		/// </returns>
		public ObjectLoader OpenObject(WindowCursor windowCursor, AnyObjectId objectId)
		{
			ObjectLoader ldr = OpenObjectImpl1(windowCursor, objectId);
			if (ldr != null)
			{
				return ldr;
			}

			ldr = OpenObjectImpl2(windowCursor, objectId.Name, objectId);
			if (ldr != null)
			{
				return ldr;
			}
			return null;
		}

		private ObjectLoader OpenObjectImpl1(WindowCursor windowCursor, AnyObjectId objectId)
		{
			ObjectLoader ldr = OpenObject1(windowCursor, objectId);
			if (ldr != null)
			{
				return ldr;
			}

			foreach (ObjectDatabase alt in GetAlternates())
			{
				ldr = alt.OpenObjectImpl1(windowCursor, objectId);
				if (ldr != null)
				{
					return ldr;
				}
			}

			if (TryAgain1())
			{
				ldr = OpenObject1(windowCursor, objectId);
				if (ldr != null)
				{
					return ldr;
				}
			}

			return null;
		}

		private ObjectLoader OpenObjectImpl2(WindowCursor windowCursor, string objectName, AnyObjectId objectId)
		{
			ObjectLoader ldr = OpenObject2(windowCursor, objectName, objectId);
			if (ldr != null)
			{
				return ldr;
			}

			foreach (ObjectDatabase alt in GetAlternates())
			{
				ldr = alt.OpenObjectImpl2(windowCursor, objectName, objectId);
				if (ldr != null)
				{
					return ldr;
				}
			}

			return null;
		}

		/// <summary>
		/// Fast half of <see cref="OpenObject"/>.
		/// </summary>
		/// <param name="windowCursor">
		/// temporary working space associated with the calling thread.
		/// </param>
		/// <param name="objectId">identity of the object to open.</param>
		/// <returns>
		/// A <see cref="ObjectLoader"/> for accessing the data of the named
		/// object, or null if the object does not exist.
		/// </returns>
		protected internal abstract ObjectLoader OpenObject1(WindowCursor windowCursor, AnyObjectId objectId);

		/// <summary>
		/// Slow half of <see cref="OpenObject"/>.
		/// </summary>
		/// <param name="windowCursor">
		/// temporary working space associated with the calling thread.
		/// </param>
		/// <param name="objectName">Name of the object to open.</param>
		/// <param name="objectId">identity of the object to open.</param>
		/// <returns>
		/// A <see cref="ObjectLoader"/> for accessing the data of the named
		/// object, or null if the object does not exist.
		/// </returns>
		protected internal virtual ObjectLoader OpenObject2(WindowCursor windowCursor, string objectName, AnyObjectId objectId)
		{
			// Assume the search took place during OpenObject1.
			return null;
		}

		/// <summary>
		/// Open the object from all packs containing it.
		/// <para />
		/// If any alternates are present, their packs are also considered.
		/// </summary>
		/// <param name="out">
		/// Result collection of loaders for this object, filled with
		/// loaders from all packs containing specified object
		/// </param>
		/// <param name="windowCursor">
		/// Temporary working space associated with the calling thread.
		/// </param>
		/// <param name="objectId"><see cref="ObjectId"/> of object to search for.</param>
		public void OpenObjectInAllPacks(ICollection<PackedObjectLoader> @out, WindowCursor windowCursor, AnyObjectId objectId)
		{
			OpenObjectInAllPacks1(@out, windowCursor, objectId);
			foreach (ObjectDatabase alt in GetAlternates())
			{
				alt.OpenObjectInAllPacks1(@out, windowCursor, objectId);
			}
		}

		/// <summary>
		/// Open the object from all packs containing it.
		/// <para />
		/// If any alternates are present, their packs are also considered.
		/// </summary>
		/// <param name="out">
		/// Result collection of loaders for this object, filled with
		/// loaders from all packs containing specified object.
		/// </param>
		/// <param name="windowCursor">
		/// Temporary working space associated with the calling thread.
		/// </param>
		/// <param name="objectId"><see cref="ObjectId"/> of object to search for.</param>
		public virtual void OpenObjectInAllPacks1(ICollection<PackedObjectLoader> @out, WindowCursor windowCursor, AnyObjectId objectId)
		{
			// Assume no pack support
		}

		/// <summary>
		/// true if the fast-half search should be tried again.
		/// </summary>
		/// <returns></returns>
		protected internal virtual bool TryAgain1()
		{
			return false;
		}

		/// <summary>
		/// Get the alternate databases known to this database.
		/// </summary>
		/// <returns>
		/// The alternate list. Never null, but may be an empty array.
		/// </returns>
		public ObjectDatabase[] GetAlternates()
		{
			ObjectDatabase[] r = _alternates.get();
			if (r == null)
			{
				lock (_alternates)
				{
					r = _alternates.get();
					if (r == null)
					{
						try
						{
							r = LoadAlternates();
						}
						catch (IOException)
						{
							r = NoAlternates;
						}

						_alternates.set(r); // [henon] possible deadlock?
					}
				}
			}
			return r;
		}

		/// <summary>
		/// Load the list of alternate databases into memory.
		/// <para />
		/// This method is invoked by <see cref="GetAlternates"/> if the alternate list
		/// has not yet been populated, or if <see cref="CloseAlternates"/> has been
		/// called on this instance and the alternate list is needed again.
		/// <para />
		/// If the alternate array is empty, implementors should consider using the
		/// constant <see cref="NoAlternates"/>.
		/// </summary>
		/// <returns>The alternate list for this database.</returns>
		/// <exception cref="Exception">
		/// The alternate list could not be accessed. The empty alternate
		/// array <see cref="NoAlternates"/> will be assumed by the caller.
		/// </exception>
		protected virtual ObjectDatabase[] LoadAlternates()
		{
			return NoAlternates;
		}

		///	<summary>
		/// Close the list of alternates returned by <seealso cref="LoadAlternates()"/>.
		///	</summary>
		///	<param name="alt">
		/// The alternate list, from <seealso cref="LoadAlternates()"/>.
		/// </param>
		protected virtual void CloseAlternates(ObjectDatabase[] alt)
		{
			foreach (ObjectDatabase d in alt)
			{
				d.Close();
			}
		}
	}
}
