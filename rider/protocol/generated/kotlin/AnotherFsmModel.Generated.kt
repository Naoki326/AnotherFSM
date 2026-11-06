@file:Suppress("EXPERIMENTAL_API_USAGE","EXPERIMENTAL_UNSIGNED_LITERALS","PackageDirectoryMismatch","UnusedImport","unused","LocalVariableName","CanBeVal","PropertyName","EnumEntryName","ClassName","ObjectPropertyName","UnnecessaryVariable","SpellCheckingInspection")
package com.anotherfsm.rider.protocol

import com.jetbrains.rd.framework.*
import com.jetbrains.rd.framework.base.*
import com.jetbrains.rd.framework.impl.*

import com.jetbrains.rd.util.lifetime.*
import com.jetbrains.rd.util.reactive.*
import com.jetbrains.rd.util.string.*
import com.jetbrains.rd.util.*
import kotlin.time.Duration
import kotlin.reflect.KClass
import kotlin.jvm.JvmStatic



/**
 * #### Generated from [AnotherFsmModel.kt]
 */
class AnotherFsmModel private constructor(
    private val _query: RdCall<String, String>,
    private val _invalidated: RdSignal<String>
) : RdExtBase() {
    //companion
    
    companion object : ISerializersOwner {
        
        override fun registerSerializersCore(serializers: ISerializers)  {
        }
        
        
        
        
        
        const val serializationHash = -85057710393708128L
        
    }
    override val serializersOwner: ISerializersOwner get() = AnotherFsmModel
    override val serializationHash: Long get() = AnotherFsmModel.serializationHash
    
    //fields
    val query: IRdCall<String, String> get() = _query
    val invalidated: IAsyncSignal<String> get() = _invalidated
    //methods
    //initializer
    init {
        _query.async = true
        _invalidated.async = true
    }
    
    init {
        bindableChildren.add("query" to _query)
        bindableChildren.add("invalidated" to _invalidated)
    }
    
    //secondary constructor
    internal constructor(
    ) : this(
        RdCall<String, String>(FrameworkMarshallers.String, FrameworkMarshallers.String),
        RdSignal<String>(FrameworkMarshallers.String)
    )
    
    //equals trait
    //hash code trait
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("AnotherFsmModel (")
        printer.indent {
            print("query = "); _query.print(printer); println()
            print("invalidated = "); _invalidated.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    override fun deepClone(): AnotherFsmModel   {
        return AnotherFsmModel(
            _query.deepClonePolymorphic(),
            _invalidated.deepClonePolymorphic()
        )
    }
    //contexts
    //threading
    override val extThreading: ExtThreadingKind get() = ExtThreadingKind.Default
}
val com.jetbrains.rd.ide.model.Solution.anotherFsmModel get() = getOrCreateExtension("anotherFsmModel", ::AnotherFsmModel)

