﻿/*
Copyright (c) 2017, Lars Brubaker, John Lewin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MatterHackers.Agg.UI;
using MatterHackers.DataConverters3D;
using MatterHackers.DataConverters3D.UndoCommands;
using MatterHackers.MatterControl.DesignTools.Operations;
using MatterHackers.PolygonMesh;

namespace MatterHackers.MatterControl.PartPreviewWindow.View3D
{
	public class MeshWrapperObject3D : Object3D
	{
		public MeshWrapperObject3D()
		{
		}

		public override bool CanApply => true;
		public override bool CanRemove => true;

		public override void Remove(UndoBuffer undoBuffer)
		{
			if (Rebuilding)
			{
				int a = 0;
			}

			SetRebuilding(true);
			// remove all the mesh wrappers that we own
			var meshWrappers = this.Descendants().Where(o => o.OwnerID == this.ID).ToList();
			foreach (var meshWrapper in meshWrappers)
			{
				meshWrapper.Remove(null);
			}
			foreach (var child in Children)
			{
				child.OutputType = PrintOutputTypes.Default;
			}

			// collapse our children into our parent
			base.Remove(null);
			SetRebuilding(false);

			Invalidate(new InvalidateArgs(this, InvalidateType.Content));
		}

		private void SetRebuilding(bool rebuilding)
		{
			foreach (var item in this.DescendantsAndSelf())
			{
				if (item is Object3D object3D)
				{
					object3D.Rebuilding = rebuilding;
				}
			}
		}

		public override void Apply(UndoBuffer undoBuffer)
		{
			var meshWrappers = this.Descendants().Where(o => o.OwnerID == this.ID).ToList();

			// remove all the meshWrappers (collapse the children)
			foreach(var meshWrapper in meshWrappers)
			{
				if (meshWrapper.Visible)
				{
					// clear the children
					meshWrapper.Children.Modify(list =>
					{
						list.Clear();
					});
					meshWrapper.OwnerID = null;
				}
				else
				{
					// remove it
					meshWrapper.Parent.Children.Remove(meshWrapper);
				}
			}

			base.Apply(undoBuffer);
		}

		public void WrapSelectedItemAndSelect(InteractiveScene scene)
		{
			if (Rebuilding)
			{
				int a = 0;
			}

			SetRebuilding(true);

			var selectedItems = scene.GetSelectedItems();

			if(selectedItems.Count > 0)
			{ 
				// cleare the selected item
				scene.SelectedItem = null;

				var clonedItemsToAdd = new List<IObject3D>(selectedItems.Select((i) => i.Clone()));

				Children.Modify((list) =>
				{
					list.Clear();

					foreach (var child in clonedItemsToAdd)
					{
						list.Add(child);
					}
				});

				AddMeshWrapperToAllChildren();

				scene.UndoBuffer.AddAndDo(
					new ReplaceCommand(
						new List<IObject3D>(selectedItems),
						new List<IObject3D> { this }));

				this.MakeNameNonColliding();

				// and select this
				scene.SelectedItem = this;
			}

			SetRebuilding(false);
			Rebuild(null);
		}

		private void AddMeshWrapperToAllChildren()
		{ 
			// Wrap every first descendant that has a mesh
			foreach (var child in this.VisibleMeshes().ToList())
			{
				// have to check that NO child of the visible mesh has us as the parent id
				if (child.object3D.OwnerID != this.ID)
				{
					// wrap the child
					child.object3D.Parent.Children.Modify((list) =>
					{
						list.Remove(child.object3D);
						list.Add(new MeshWrapper(child.object3D, this.ID));
					});
				}
			}
		}

		public IEnumerable<(IObject3D original, IObject3D meshCopy)> MeshObjects()
		{
			return this.Descendants()
				.Where((obj) => obj.OwnerID == this.ID)
				.Select((mw) => (mw.Children.First(), mw));
		}

		public void ResetMeshWrapperMeshes(Object3DPropertyFlags flags, CancellationToken cancellationToken)
		{
			// if there are not already, wrap all meshes with our id (some inner object may have changed it's meshes)
			AddMeshWrapperToAllChildren();

			this.Mesh = null;
			var participants = this.Descendants().Where(o => o.OwnerID == this.ID).ToList();
			foreach (var item in participants)
			{
				var firstChild = item.Children.First();
				// set the mesh back to a copy of the child mesh
				item.Mesh = Mesh.Copy(firstChild.Mesh, cancellationToken);
				// and reset the properties
				item.CopyProperties(firstChild, flags & (~Object3DPropertyFlags.Matrix));
			}
		}
	}
}