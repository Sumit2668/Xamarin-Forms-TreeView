﻿using System;
using System.Linq;
using System.ComponentModel;
using System.Collections;
using System.Collections.Generic;

namespace HighEnergy.Collections
{
    public class TreeNode<T> : ObservableObject, ITreeNode<T>, IDisposable
        where T : new()
    {
        public TreeNode()
        {
            // call property setters to trigger setup and event notifications
            _Parent = null;
            ChildNodes = new TreeNodeList<T>(this);
        }

        public TreeNode(T Value)
        {
            // call property setters to trigger setup and event notifications
            this.Value = Value;
            _Parent = null;
            ChildNodes = new TreeNodeList<T>(this);
        }

        public TreeNode(T Value, TreeNode<T> Parent)
        {
            // call property setters to trigger setup and event notifications
            this.Value = Value;
            _Parent = Parent;
            ChildNodes = new TreeNodeList<T>(this);
        }

        public ITreeNode ParentNode 
        { 
            get { return _Parent; } 
        }

        private ITreeNode<T> _Parent;
        public ITreeNode<T> Parent
        {
            get { return _Parent; }
            set { SetParent(value, true); }
        }

        public void SetParent(ITreeNode<T> node, bool updateChildNodes = true)
        {
            if (node == Parent)
                return;

            var oldParent = Parent;
            var oldParentHeight = Parent != null ? Parent.Height : 0;
            var oldDepth = Depth;

            // if oldParent isn't null
            // remove this node from its newly ex-parent's children
            if (oldParent != null && oldParent.ChildNodes.Contains(this))
                oldParent.ChildNodes.Remove(this, updateParent: false);

            // update the backing field
            _Parent = node;

            // add this node to its new parent's children
            if (_Parent != null && updateChildNodes)
                _Parent.ChildNodes.Add(this, updateParent: false);

            // signal the old parent that it has lost this child
            if (oldParent != null)
                oldParent.OnDescendantChanged(NodeChangeType.ChildRemoved, this);

            if (oldDepth != Depth)
                OnDepthChanged();

            // if this operation has changed the height of any parent, initiate the bubble-up height changed event
            var newParentHeight = Parent != null ? Parent.Height : 0;
            if (Parent != null && newParentHeight != oldParentHeight)
                Parent.OnHeightChanged();

            OnParentChanged(oldParent, Parent);
        }

        protected virtual void OnParentChanged(ITreeNode<T> oldValue, ITreeNode<T> newValue)
        {
            OnPropertyChanged("Parent");
        }

        // TODO: add property and event notifications that are missing from this set: DescendentsChanged, AnscestorsChanged, ChildrenChanged, ParentChanged

        public ITreeNode<T> Root
        {
            get { return (Parent == null) ? this : Parent.Root; }
        }

        private TreeNodeList<T> _ChildNodes;
        public TreeNodeList<T> ChildNodes
        {
            get { return _ChildNodes; }
            private set
            {
                if (value == _ChildNodes)
                    return;

                if (_ChildNodes != null)
                    _ChildNodes.PropertyChanged -= HandleChildNodeCountChange;

                _ChildNodes = value;

                if (_ChildNodes != null)
                    _ChildNodes.PropertyChanged += HandleChildNodeCountChange;

                OnPropertyChanged("ChildNodes");
            }
        }

        void HandleChildNodeCountChange(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Count")
                OnPropertyChanged("Count");
        }

        // non-generic iterator for interface-based support
        public IEnumerable<ITreeNode> Children
        {
            get
            {
                foreach (ITreeNode node in ChildNodes)
                    yield return node;

                yield break;
            }
        }

        public IEnumerable<ITreeNode> Descendants
        {
            get
            {
                foreach (ITreeNode node in Children)
                {
                    yield return node;

                    foreach (ITreeNode descendant in node.Descendants)
                        yield return descendant;
                }

                yield break;
            }
        }

        public IEnumerable<ITreeNode> Subtree
        {
            get
            {
                yield return this;

                foreach (ITreeNode node in Descendants)
                    yield return node;

                yield break;
            }
        }

        public IEnumerable<ITreeNode> Ancestors
        {
            get
            {
                if (Parent == null)
                    yield break;

                yield return Parent;

                foreach (ITreeNode node in Parent.Ancestors)
                    yield return node;

                yield break;
            }
        }

        public virtual void OnAncestorChanged(NodeChangeType changeType, ITreeNode node)
        {
            foreach (ITreeNode<T> child in ChildNodes)
                child.OnAncestorChanged(changeType, node);
        }

        public virtual void OnDescendantChanged(NodeChangeType changeType, ITreeNode node)
        {
            if (Parent != null)
                Parent.OnDescendantChanged(changeType, node);
        }

        // [recurse up] descending aggregate property
        public int Height
        {
            get { return ChildNodes.Count == 0 ? 0 : ChildNodes.Max(n => n.Height) + 1; }
        }

        // [recurse down] descending-broadcasting event
        public virtual void OnHeightChanged()
        {
            OnPropertyChanged("Height");

            foreach (ITreeNode<T> child in ChildNodes)
                child.OnHeightChanged();
        }

        private T _Value;
        public T Value
        {
            get { return _Value; }
            set
            {
                if (value == null && _Value == null)
                    return;

                if (value != null && _Value != null && value.Equals(_Value))
                    return;

                _Value = value;
                OnPropertyChanged("Value");

                // set Node if it's ITreeNodeAware
                if (_Value != null && _Value is ITreeNodeAware<T>)
                    (_Value as ITreeNodeAware<T>).Node = this;
            }
        }

        // [recurse up] bubble up aggregate property
        public int Depth
        {
            get { return (Parent == null ? 0 : Parent.Depth + 1); }
        }

        // [recurse up] bubble up event
        public virtual void OnDepthChanged()
        {
            OnPropertyChanged("Depth");

            if (Parent != null)
                Parent.OnDepthChanged();
        }

        private UpDownTraversalType _DisposeTraversal = UpDownTraversalType.BottomUp;
        public UpDownTraversalType DisposeTraversal
        {
            get { return _DisposeTraversal; }
            set { _DisposeTraversal = value; }
        }

        private bool _IsDisposed;
        public bool IsDisposed
        {
            get { return _IsDisposed; }
        }

        public virtual void Dispose()
        {
            CheckDisposed();
            OnDisposing();

            // clean up contained objects (in Value property)
            if (Value is IDisposable)
            {
                if (DisposeTraversal == UpDownTraversalType.BottomUp)
                    foreach (TreeNode<T> node in ChildNodes)
                        node.Dispose();

                (Value as IDisposable).Dispose();

                if (DisposeTraversal == UpDownTraversalType.TopDown)
                    foreach (TreeNode<T> node in ChildNodes)
                        node.Dispose();
            }

            _IsDisposed = true;
        }

        public event EventHandler Disposing;

        protected void OnDisposing()
        {
            if (Disposing != null)
                Disposing(this, EventArgs.Empty);
        }

        public void CheckDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        public override string ToString()
        {
            return "Depth=" + Depth + ", Height=" + Height + ", Children=" + ChildNodes.Count;
        }
    }
}